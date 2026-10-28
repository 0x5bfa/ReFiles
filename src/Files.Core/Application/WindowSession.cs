// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>
/// Owns the tab collection and active-tab state for one application window.
/// </summary>
public sealed class WindowSession : IAsyncDisposable
{
	private readonly IBrowsePaneSessionFactory _paneFactory;

	private readonly Lock _syncRoot = new();

	private readonly Lock _disposalLock = new();

	private readonly SemaphoreSlim _mutationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly List<TabSession> _tabs = [];

	private readonly Stack<TabRestoreState> _closedTabs = [];

	private IReadOnlyList<TabSession> _tabSnapshot = [];

	private Guid _activeTabId;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	/// <summary>Gets the stable window identifier.</summary>
	public Guid Id { get; }

	/// <summary>Gets the owned tab sessions.</summary>
	public IReadOnlyList<TabSession> Tabs => Volatile.Read(ref _tabSnapshot);

	/// <summary>Gets the active tab session.</summary>
	public TabSession? ActiveTab
	{
		get
		{
			lock (_syncRoot)
			{
				return _tabs.FirstOrDefault(tab => tab.Id == _activeTabId);
			}
		}
	}

	/// <summary>Gets whether a closed tab is available for restoration.</summary>
	public bool CanReopenTab
	{
		get
		{
			lock (_syncRoot)
			{
				return _closedTabs.Count > 0;
			}
		}
	}

	/// <summary>Occurs when the tab collection or ordering changes.</summary>
	public event EventHandler? TabsChanged;

	/// <summary>Occurs when the active tab changes.</summary>
	public event EventHandler? ActiveTabChanged;

	/// <summary>Initializes an empty window session.</summary>
	/// <param name="paneFactory">The factory used for browse panes.</param>
	/// <param name="id">An optional stable window identifier.</param>
	public WindowSession(IBrowsePaneSessionFactory paneFactory, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A window ID cannot be empty.", nameof(id));
		}

		_paneFactory = paneFactory;
	}

	/// <summary>Opens and activates a tab.</summary>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created tab session.</returns>
	public async ValueTask<TabSession> OpenTabAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		PaneSession? pane = null;
		TabSession? tab = null;

		try
		{
			EnsureActive();
			pane = _paneFactory.Create();
			if (initialLocation is not null)
			{
				await GetBrowseContent(pane).NavigateAsync(initialLocation, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
			}

			tab = new TabSession(_paneFactory, pane);
			pane = null;

			lock (_syncRoot)
			{
				EnsureActive();
				_tabs.Add(tab);
				_activeTabId = tab.Id;
				UpdateSnapshot();
			}

			var result = tab;
			tab = null;
			SessionEvent.Raise(this, TabsChanged);
			SessionEvent.Raise(this, ActiveTabChanged);

			return result;
		}
		catch (Exception creationError)
		{
			var incompleteModel = (IAsyncDisposable?)tab ?? pane;
			if (incompleteModel is null)
			{
				throw;
			}

			try
			{
				await incompleteModel.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Tab creation and cleanup failed.", creationError, cleanupError);
			}

			throw;
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	/// <summary>Opens and activates a tab containing custom pane content.</summary>
	/// <param name="contentFactory">The factory used to create the owned pane content.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created tab session.</returns>
	public ValueTask<TabSession> OpenContentTabAsync(Func<IPaneContentSession> contentFactory, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(contentFactory);

		return OpenRestoredTabAsync(new TabRestoreState([contentFactory], 0, PaneSplitOrientation.None), cancellationToken);
	}

	/// <summary>Opens an independently owned tab from captured state.</summary>
	/// <param name="state">The complete tab state.</param>
	/// <param name="cancellationToken">The restoration cancellation token.</param>
	/// <returns>The restored tab, published only after every pane initializes.</returns>
	public async ValueTask<TabSession> OpenRestoredTabAsync(TabRestoreState state, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(state);

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
		try
		{
			EnsureActive();

			return await OpenRestoredTabCoreAsync(state, linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	/// <summary>Duplicates every pane and the layout of an existing tab.</summary>
	/// <param name="tabId">The tab identity.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The duplicate, or null if the tab is absent or cannot be restored.</returns>
	public async ValueTask<TabSession?> DuplicateTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		TabRestoreState? state;
		lock (_syncRoot)
		{
			EnsureActive();

			state = _tabs.FirstOrDefault(tab => tab.Id == tabId)?.CaptureRestoreState();
		}

		return state is null ? null : await OpenRestoredTabAsync(state, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Restores the most recently closed tab, retaining its state if restoration fails.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The restored tab, or null when no state is available.</returns>
	public async ValueTask<TabSession?> ReopenTabAsync(CancellationToken cancellationToken = default)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
		try
		{
			TabRestoreState state;
			lock (_syncRoot)
			{
				EnsureActive();

				if (!_closedTabs.TryPeek(out state!))
				{
					return null;
				}
			}

			return await OpenRestoredTabCoreAsync(state, linkedCancellation.Token, consumeClosedState: true).ConfigureAwait(false);
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	/// <summary>Closes and disposes a tab.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the tab existed.</returns>
	public async ValueTask<bool> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		if (tabId == Guid.Empty)
		{
			throw new ArgumentException("A tab ID is required.", nameof(tabId));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		TabSession? removed = null;
		var activeTabChanged = false;
		try
		{
			EnsureActive();

			lock (_syncRoot)
			{
				var index = _tabs.FindIndex(tab => tab.Id == tabId);
				if (index < 0)
				{
					return false;
				}

				removed = _tabs[index];
				if (removed.CaptureRestoreState() is { } state)
				{
					_closedTabs.Push(state);
				}

				_tabs.RemoveAt(index);
				if (_activeTabId == tabId)
				{
					activeTabChanged = true;
					_activeTabId = _tabs.Count is 0
						? Guid.Empty
						: _tabs[Math.Min(index, _tabs.Count - 1)].Id;
				}

				UpdateSnapshot();
			}

			SessionEvent.Raise(this, TabsChanged);
			if (activeTabChanged)
			{
				SessionEvent.Raise(this, ActiveTabChanged);
			}

			var ownedTab = removed!;
			removed = null;
			await ownedTab.DisposeAsync().ConfigureAwait(false);

			return true;
		}
		finally
		{
			_mutationLock.Release();
			if (removed is not null)
			{
				await removed.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	/// <summary>Activates a tab.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <returns><see langword="true"/> when the tab exists.</returns>
	public bool SetActiveTab(Guid tabId)
	{
		if (tabId == Guid.Empty)
		{
			throw new ArgumentException("A tab ID is required.", nameof(tabId));
		}

		var changed = false;

		lock (_syncRoot)
		{
			EnsureActive();
			if (!_tabs.Any(tab => tab.Id == tabId))
			{
				return false;
			}

			if (_activeTabId != tabId)
			{
				_activeTabId = tabId;
				changed = true;
			}
		}

		if (changed)
		{
			SessionEvent.Raise(this, ActiveTabChanged);
		}

		return true;
	}

	/// <summary>Moves a tab within the window.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <param name="targetIndex">The destination index.</param>
	/// <returns><see langword="true"/> when the tab exists.</returns>
	public bool MoveTab(Guid tabId, int targetIndex)
	{
		if (tabId == Guid.Empty)
		{
			throw new ArgumentException("A tab ID is required.", nameof(tabId));
		}

		var changed = false;

		lock (_syncRoot)
		{
			EnsureActive();
			var currentIndex = _tabs.FindIndex(tab => tab.Id == tabId);
			if (currentIndex < 0)
			{
				return false;
			}

			if (targetIndex < 0 || targetIndex >= _tabs.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(targetIndex));
			}

			if (currentIndex != targetIndex)
			{
				var tab = _tabs[currentIndex];
				_tabs.RemoveAt(currentIndex);
				_tabs.Insert(targetIndex, tab);
				UpdateSnapshot();
				changed = true;
			}
		}

		if (changed)
		{
			SessionEvent.Raise(this, TabsChanged);
		}

		return true;
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_isDisposed = true;
			_lifetime.Cancel();
			_disposeTask = DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await _mutationLock.WaitAsync().ConfigureAwait(false);
		TabSession[] ownedTabs;

		try
		{
			lock (_syncRoot)
			{
				ownedTabs = [.. _tabs];
				_tabs.Clear();
				_closedTabs.Clear();
				_activeTabId = Guid.Empty;
				UpdateSnapshot();
			}
		}
		finally
		{
			_mutationLock.Release();
		}

		List<Exception>? errors = null;
		foreach (var tab in ownedTabs.Reverse())
		{
			try
			{
				await tab.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}
		}

		_mutationLock.Dispose();
		_lifetime.Dispose();
		GC.SuppressFinalize(this);

		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("One or more window tabs could not be disposed.", errors);
		}
	}

	private async ValueTask<TabSession> OpenRestoredTabCoreAsync(TabRestoreState state, CancellationToken cancellationToken, bool consumeClosedState = false)
	{
		var tab = await TabSession.RestoreAsync(_paneFactory, state, cancellationToken).ConfigureAwait(false);
		try
		{
			lock (_syncRoot)
			{
				EnsureActive();

				_tabs.Add(tab);
				if (consumeClosedState)
				{
					_closedTabs.Pop();
				}

				_activeTabId = tab.Id;
				UpdateSnapshot();
			}
		}
		catch
		{
			await tab.DisposeAsync().ConfigureAwait(false);
			throw;
		}

		SessionEvent.Raise(this, TabsChanged);
		SessionEvent.Raise(this, ActiveTabChanged);

		return tab;
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _tabSnapshot, Array.AsReadOnly(_tabs.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

	private static BrowsePaneSession GetBrowseContent(PaneSession pane)
	{
		return pane.Content as BrowsePaneSession ?? throw new InvalidOperationException("The browse pane factory returned a pane with unsupported content.");
	}
}

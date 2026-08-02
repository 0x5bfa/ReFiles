// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Owns the tab collection and active-tab state for one application window.
/// </summary>
public sealed class WindowModel : IAsyncDisposable
{
	private readonly IBrowsePaneFactory _paneFactory;

	private readonly Lock _syncRoot = new();

	private readonly Lock _disposalLock = new();

	private readonly SemaphoreSlim _mutationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly List<TabModel> _tabs = [];

	private IReadOnlyList<TabModel> _tabSnapshot = [];

	private Guid _activeTabId;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	public Guid Id { get; }

	public IReadOnlyList<TabModel> Tabs => Volatile.Read(ref _tabSnapshot);

	public TabModel? ActiveTab
	{
		get
		{
			lock (_syncRoot)
			{
				return _tabs.FirstOrDefault(tab => tab.Id == _activeTabId);
			}
		}
	}

	public event EventHandler? StateChanged;

	public WindowModel(IBrowsePaneFactory paneFactory, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A window ID cannot be empty.", nameof(id));
		}

		_paneFactory = paneFactory;
	}

	public async ValueTask<TabModel> OpenTabAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		PaneModel? pane = null;
		TabModel? tab = null;

		try
		{
			EnsureActive();
			pane = _paneFactory.Create();
			if (initialLocation is not null)
			{
				await pane.NavigateAsync(initialLocation, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
			}

			tab = new TabModel(_paneFactory, pane);
			pane = null;

			lock (_syncRoot)
			{
				EnsureActive();
				_tabs.Add(tab);
				tab.StateChanged += OnTabStateChanged;
				_activeTabId = tab.Id;
				UpdateSnapshot();
			}

			var result = tab;
			tab = null;
			ModelEvent.Raise(this, StateChanged);

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

	public async ValueTask<bool> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		if (tabId == Guid.Empty)
		{
			throw new ArgumentException("A tab ID is required.", nameof(tabId));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		TabModel? removed = null;
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
				_tabs.RemoveAt(index);
				removed.StateChanged -= OnTabStateChanged;
				if (_activeTabId == tabId)
				{
					_activeTabId = _tabs.Count is 0
						? Guid.Empty
						: _tabs[Math.Min(index, _tabs.Count - 1)].Id;
				}

				UpdateSnapshot();
			}

			ModelEvent.Raise(this, StateChanged);
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
			ModelEvent.Raise(this, StateChanged);
		}

		return true;
	}

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
			ModelEvent.Raise(this, StateChanged);
		}

		return true;
	}

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
		TabModel[] ownedTabs;

		try
		{
			lock (_syncRoot)
			{
				ownedTabs = _tabs.ToArray();
				foreach (var tab in ownedTabs)
				{
					tab.StateChanged -= OnTabStateChanged;
				}

				_tabs.Clear();
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

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _tabSnapshot, Array.AsReadOnly(_tabs.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

	private void OnTabStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Specifies the split layout of a tab.</summary>
public enum PaneSplitOrientation
{
	/// <summary>The tab contains one pane.</summary>
	None,

	/// <summary>The panes are arranged side by side.</summary>
	Vertical,

	/// <summary>The panes are arranged one above the other.</summary>
	Horizontal,
}

/// <summary>
/// Owns one or two panes and the active-pane state for a tab.
/// </summary>
public sealed class TabSession : IAsyncDisposable
{
	private readonly IBrowsePaneSessionFactory _paneFactory;

	private readonly Lock _syncRoot = new();

	private readonly Lock _disposalLock = new();

	private readonly SemaphoreSlim _mutationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly List<PaneSession> _panes;

	private IReadOnlyList<PaneSession> _paneSnapshot;

	private Guid _activePaneId;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	private PaneSplitOrientation _splitOrientation;

	/// <summary>Gets the stable tab identifier.</summary>
	public Guid Id { get; }

	/// <summary>Gets the owned pane sessions.</summary>
	public IReadOnlyList<PaneSession> Panes => Volatile.Read(ref _paneSnapshot);

	/// <summary>Gets the active pane session.</summary>
	public PaneSession? ActivePane
	{
		get
		{
			lock (_syncRoot)
			{
				return _panes.FirstOrDefault(pane => pane.Id == _activePaneId);
			}
		}
	}

	/// <summary>Gets the current split orientation.</summary>
	public PaneSplitOrientation SplitOrientation
	{
		get
		{
			lock (_syncRoot)
			{
				return _splitOrientation;
			}
		}
	}

	/// <summary>Occurs when the pane collection changes.</summary>
	public event EventHandler? PanesChanged;

	/// <summary>Occurs when the active pane changes.</summary>
	public event EventHandler? ActivePaneChanged;

	/// <summary>Occurs when the split orientation changes.</summary>
	public event EventHandler? SplitOrientationChanged;

	/// <summary>Initializes a tab that owns its primary pane.</summary>
	/// <param name="paneFactory">The factory used to create split panes.</param>
	/// <param name="primaryPane">The primary pane to own.</param>
	/// <param name="id">An optional stable tab identifier.</param>
	public TabSession(IBrowsePaneSessionFactory paneFactory, PaneSession primaryPane, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);
		ArgumentNullException.ThrowIfNull(primaryPane);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A tab ID cannot be empty.", nameof(id));
		}

		_paneFactory = paneFactory;
		_panes = [primaryPane];
		_paneSnapshot = Array.AsReadOnly(_panes.ToArray());
		_activePaneId = primaryPane.Id;
	}

	/// <summary>Opens and activates a second pane.</summary>
	/// <param name="orientation">The split orientation.</param>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created pane session.</returns>
	public async ValueTask<PaneSession> OpenSplitAsync(PaneSplitOrientation orientation, BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		if (orientation is not PaneSplitOrientation.Vertical and not PaneSplitOrientation.Horizontal)
		{
			throw new ArgumentOutOfRangeException(nameof(orientation), "A split pane requires a vertical or horizontal orientation.");
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		PaneSession? pane = null;

		try
		{
			EnsureActive();

			lock (_syncRoot)
			{
				if (_panes.Count >= 2)
				{
					throw new InvalidOperationException("A tab cannot contain more than two panes.");
				}

				initialLocation ??= _panes.FirstOrDefault(candidate => candidate.Id == _activePaneId) is { } activePane ? GetBrowseContent(activePane).Location : null;
			}

			pane = _paneFactory.Create();
			if (initialLocation is not null)
			{
				await GetBrowseContent(pane).NavigateAsync(initialLocation, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
			}

			lock (_syncRoot)
			{
				EnsureActive();
				_panes.Add(pane);
				_activePaneId = pane.Id;
				_splitOrientation = orientation;
				UpdateSnapshot();
			}

			var result = pane;
			pane = null;
			SessionEvent.Raise(this, PanesChanged);
			SessionEvent.Raise(this, ActivePaneChanged);
			SessionEvent.Raise(this, SplitOrientationChanged);

			return result;
		}
		catch (Exception creationError)
		{
			if (pane is null)
			{
				throw;
			}

			try
			{
				await pane.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Split-pane creation and cleanup failed.", creationError, cleanupError);
			}

			throw;
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	/// <summary>Closes and disposes a pane.</summary>
	/// <param name="paneId">The pane identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the pane could be closed.</returns>
	public async ValueTask<bool> ClosePaneAsync(Guid paneId, CancellationToken cancellationToken = default)
	{
		if (paneId == Guid.Empty)
		{
			throw new ArgumentException("A pane ID is required.", nameof(paneId));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		PaneSession? removed = null;
		var activePaneChanged = false;

		try
		{
			EnsureActive();

			lock (_syncRoot)
			{
				if (_panes.Count <= 1)
				{
					return false;
				}

				var index = _panes.FindIndex(pane => pane.Id == paneId);
				if (index < 0)
				{
					return false;
				}

				removed = _panes[index];
				_panes.RemoveAt(index);
				if (_activePaneId == paneId)
				{
					activePaneChanged = true;
					_activePaneId = _panes[Math.Min(index, _panes.Count - 1)].Id;
				}

				_splitOrientation = PaneSplitOrientation.None;
				UpdateSnapshot();
			}

			SessionEvent.Raise(this, PanesChanged);
			if (activePaneChanged)
			{
				SessionEvent.Raise(this, ActivePaneChanged);
			}

			SessionEvent.Raise(this, SplitOrientationChanged);
			var ownedPane = removed!;
			removed = null;
			await ownedPane.DisposeAsync().ConfigureAwait(false);

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

	/// <summary>Activates a pane.</summary>
	/// <param name="paneId">The pane identifier.</param>
	/// <returns><see langword="true"/> when the pane exists.</returns>
	public bool SetActivePane(Guid paneId)
	{
		if (paneId == Guid.Empty)
		{
			throw new ArgumentException("A pane ID is required.", nameof(paneId));
		}

		var changed = false;

		lock (_syncRoot)
		{
			EnsureActive();
			if (!_panes.Any(pane => pane.Id == paneId))
			{
				return false;
			}

			if (_activePaneId != paneId)
			{
				_activePaneId = paneId;
				changed = true;
			}
		}

		if (changed)
		{
			SessionEvent.Raise(this, ActivePaneChanged);
		}

		return true;
	}

	/// <summary>Changes the orientation of an existing split.</summary>
	/// <param name="orientation">The new split orientation.</param>
	/// <returns><see langword="true"/> when the tab is split.</returns>
	public bool SetSplitOrientation(PaneSplitOrientation orientation)
	{
		if (orientation is not PaneSplitOrientation.Vertical and not PaneSplitOrientation.Horizontal)
		{
			throw new ArgumentOutOfRangeException(nameof(orientation), "Close the secondary pane to remove a split.");
		}

		var changed = false;
		lock (_syncRoot)
		{
			EnsureActive();

			if (_panes.Count is not 2)
			{
				return false;
			}

			if (_splitOrientation != orientation)
			{
				_splitOrientation = orientation;
				changed = true;
			}
		}

		if (changed)
		{
			SessionEvent.Raise(this, SplitOrientationChanged);
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
		PaneSession[] ownedPanes;

		try
		{
			lock (_syncRoot)
			{
				ownedPanes = [.. _panes];
				_panes.Clear();
				_activePaneId = Guid.Empty;
				_splitOrientation = PaneSplitOrientation.None;
				UpdateSnapshot();
			}
		}
		finally
		{
			_mutationLock.Release();
		}

		List<Exception>? errors = null;
		foreach (var pane in ownedPanes.Reverse())
		{
			try
			{
				await pane.DisposeAsync().ConfigureAwait(false);
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
			throw new AggregateException("One or more tab panes could not be disposed.", errors);
		}
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _paneSnapshot, Array.AsReadOnly(_panes.ToArray()));
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

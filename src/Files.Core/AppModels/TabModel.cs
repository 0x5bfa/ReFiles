// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

public enum PaneSplitOrientation
{
	None,
	Vertical,
	Horizontal,
}

/// <summary>
/// Owns one or two panes and the active-pane state for a tab.
/// </summary>
public sealed class TabModel : IAsyncDisposable
{
	private readonly IBrowsePaneFactory paneFactory;
	private readonly object syncRoot = new();
	private readonly object disposalLock = new();
	private readonly SemaphoreSlim mutationLock = new(1, 1);
	private readonly CancellationTokenSource lifetime = new();
	private readonly List<PaneModel> panes;
	private IReadOnlyList<PaneModel> paneSnapshot;
	private Guid activePaneId;
	private Task? disposeTask;
	private volatile bool isDisposed;
	private PaneSplitOrientation splitOrientation;

	public TabModel(IBrowsePaneFactory paneFactory, PaneModel primaryPane, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);
		ArgumentNullException.ThrowIfNull(primaryPane);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A tab ID cannot be empty.", nameof(id));
		}

		this.paneFactory = paneFactory;
		panes = [primaryPane];
		paneSnapshot = Array.AsReadOnly(panes.ToArray());
		activePaneId = primaryPane.Id;
		primaryPane.StateChanged += OnPaneStateChanged;
	}

	public Guid Id { get; }

	public IReadOnlyList<PaneModel> Panes =>
		Volatile.Read(ref paneSnapshot);

	public PaneModel? ActivePane
	{
		get
		{
			lock (syncRoot)
			{
				return panes.FirstOrDefault(pane => pane.Id == activePaneId);
			}
		}
	}

	public PaneSplitOrientation SplitOrientation
	{
		get
		{
			lock (syncRoot)
			{
				return splitOrientation;
			}
		}
	}

	public event EventHandler? StateChanged;

	public async ValueTask<PaneModel> OpenSplitAsync(
		PaneSplitOrientation orientation,
		BrowseLocation? initialLocation = null,
		CancellationToken cancellationToken = default)
	{
		if (orientation is not PaneSplitOrientation.Vertical
			and not PaneSplitOrientation.Horizontal)
		{
			throw new ArgumentOutOfRangeException(nameof(orientation), "A split pane requires a vertical or horizontal orientation.");
		}

		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		PaneModel? pane = null;
		try
		{
			EnsureActive();
			lock (syncRoot)
			{
				if (panes.Count >= 2)
				{
					throw new InvalidOperationException("A tab cannot contain more than two panes.");
				}

				initialLocation ??= panes
					.FirstOrDefault(candidate => candidate.Id == activePaneId)
					?.Location;
			}

			pane = paneFactory.Create();
			if (initialLocation is not null)
			{
				await pane
					.NavigateAsync(initialLocation, cancellationToken: linkedCancellation.Token)
					.ConfigureAwait(false);
			}

			lock (syncRoot)
			{
				EnsureActive();
				panes.Add(pane);
				pane.StateChanged += OnPaneStateChanged;
				activePaneId = pane.Id;
				splitOrientation = orientation;
				UpdateSnapshot();
			}

			var result = pane;
			pane = null;
			ModelEvent.Raise(this, StateChanged);
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
			mutationLock.Release();
		}
	}

	public async ValueTask<bool> ClosePaneAsync(Guid paneId, CancellationToken cancellationToken = default)
	{
		if (paneId == Guid.Empty)
		{
			throw new ArgumentException("A pane ID is required.", nameof(paneId));
		}

		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		PaneModel? removed = null;
		try
		{
			EnsureActive();
			lock (syncRoot)
			{
				if (panes.Count <= 1)
				{
					return false;
				}

				var index = panes.FindIndex(pane => pane.Id == paneId);
				if (index < 0)
				{
					return false;
				}

				removed = panes[index];
				panes.RemoveAt(index);
				removed.StateChanged -= OnPaneStateChanged;
				if (activePaneId == paneId)
				{
					activePaneId = panes[Math.Min(index, panes.Count - 1)].Id;
				}

				splitOrientation = PaneSplitOrientation.None;
				UpdateSnapshot();
			}

			ModelEvent.Raise(this, StateChanged);
			var ownedPane = removed!;
			removed = null;
			await ownedPane.DisposeAsync().ConfigureAwait(false);
			return true;
		}
		finally
		{
			mutationLock.Release();
			if (removed is not null)
			{
				await removed.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	public bool SetActivePane(Guid paneId)
	{
		if (paneId == Guid.Empty)
		{
			throw new ArgumentException("A pane ID is required.", nameof(paneId));
		}

		var changed = false;
		lock (syncRoot)
		{
			EnsureActive();
			if (!panes.Any(pane => pane.Id == paneId))
			{
				return false;
			}

			if (activePaneId != paneId)
			{
				activePaneId = paneId;
				changed = true;
			}
		}

		if (changed)
		{
			ModelEvent.Raise(this, StateChanged);
		}

		return true;
	}

	public bool SetSplitOrientation(PaneSplitOrientation orientation)
	{
		if (orientation is not PaneSplitOrientation.Vertical
			and not PaneSplitOrientation.Horizontal)
		{
			throw new ArgumentOutOfRangeException(nameof(orientation), "Close the secondary pane to remove a split.");
		}

		var changed = false;
		lock (syncRoot)
		{
			EnsureActive();
			if (panes.Count is not 2)
			{
				return false;
			}

			if (splitOrientation != orientation)
			{
				splitOrientation = orientation;
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
		lock (disposalLock)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			isDisposed = true;
			lifetime.Cancel();
			disposeTask = DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		await mutationLock.WaitAsync().ConfigureAwait(false);
		PaneModel[] ownedPanes;
		try
		{
			lock (syncRoot)
			{
				ownedPanes = panes.ToArray();
				foreach (var pane in ownedPanes)
				{
					pane.StateChanged -= OnPaneStateChanged;
				}

				panes.Clear();
				activePaneId = Guid.Empty;
				splitOrientation = PaneSplitOrientation.None;
				UpdateSnapshot();
			}
		}
		finally
		{
			mutationLock.Release();
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

		mutationLock.Dispose();
		lifetime.Dispose();
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
		Volatile.Write(ref paneSnapshot, Array.AsReadOnly(panes.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}

	private void OnPaneStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}
}

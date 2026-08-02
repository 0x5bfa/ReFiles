// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Owns the tab collection and active-tab state for one application window.
/// </summary>
public sealed class WindowModel : IAsyncDisposable
{
	private readonly IBrowsePaneFactory paneFactory;
	private readonly object syncRoot = new();
	private readonly object disposalLock = new();
	private readonly SemaphoreSlim mutationLock = new(1, 1);
	private readonly CancellationTokenSource lifetime = new();
	private readonly List<TabModel> tabs = [];
	private IReadOnlyList<TabModel> tabSnapshot =
		Array.Empty<TabModel>();
	private Guid activeTabId;
	private Task? disposeTask;
	private volatile bool isDisposed;

	public WindowModel(IBrowsePaneFactory paneFactory, Guid? id = null)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);

		Id = id ?? Guid.NewGuid();
		if (Id == Guid.Empty)
		{
			throw new ArgumentException("A window ID cannot be empty.", nameof(id));
		}

		this.paneFactory = paneFactory;
	}

	public Guid Id { get; }

	public IReadOnlyList<TabModel> Tabs =>
		Volatile.Read(ref tabSnapshot);

	public TabModel? ActiveTab
	{
		get
		{
			lock (syncRoot)
			{
				return tabs.FirstOrDefault(tab => tab.Id == activeTabId);
			}
		}
	}

	public event EventHandler? StateChanged;

	public async ValueTask<TabModel> OpenTabAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		PaneModel? pane = null;
		TabModel? tab = null;
		try
		{
			EnsureActive();
			pane = paneFactory.Create();
			if (initialLocation is not null)
			{
				await pane
					.NavigateAsync(initialLocation, cancellationToken: linkedCancellation.Token)
					.ConfigureAwait(false);
			}

			tab = new TabModel(paneFactory, pane);
			pane = null;

			lock (syncRoot)
			{
				EnsureActive();
				tabs.Add(tab);
				tab.StateChanged += OnTabStateChanged;
				activeTabId = tab.Id;
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
			mutationLock.Release();
		}
	}

	public async ValueTask<bool> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
	{
		if (tabId == Guid.Empty)
		{
			throw new ArgumentException("A tab ID is required.", nameof(tabId));
		}

		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		TabModel? removed = null;
		try
		{
			EnsureActive();
			lock (syncRoot)
			{
				var index = tabs.FindIndex(tab => tab.Id == tabId);
				if (index < 0)
				{
					return false;
				}

				removed = tabs[index];
				tabs.RemoveAt(index);
				removed.StateChanged -= OnTabStateChanged;
				if (activeTabId == tabId)
				{
					activeTabId = tabs.Count is 0
						? Guid.Empty
						: tabs[Math.Min(index, tabs.Count - 1)].Id;
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
			mutationLock.Release();
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
		lock (syncRoot)
		{
			EnsureActive();
			if (!tabs.Any(tab => tab.Id == tabId))
			{
				return false;
			}

			if (activeTabId != tabId)
			{
				activeTabId = tabId;
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
		lock (syncRoot)
		{
			EnsureActive();
			var currentIndex = tabs.FindIndex(tab => tab.Id == tabId);
			if (currentIndex < 0)
			{
				return false;
			}

			if (targetIndex < 0 || targetIndex >= tabs.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(targetIndex));
			}

			if (currentIndex != targetIndex)
			{
				var tab = tabs[currentIndex];
				tabs.RemoveAt(currentIndex);
				tabs.Insert(targetIndex, tab);
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
		TabModel[] ownedTabs;
		try
		{
			lock (syncRoot)
			{
				ownedTabs = tabs.ToArray();
				foreach (var tab in ownedTabs)
				{
					tab.StateChanged -= OnTabStateChanged;
				}

				tabs.Clear();
				activeTabId = Guid.Empty;
				UpdateSnapshot();
			}
		}
		finally
		{
			mutationLock.Release();
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

		mutationLock.Dispose();
		lifetime.Dispose();
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
		Volatile.Write(ref tabSnapshot, Array.AsReadOnly(tabs.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}

	private void OnTabStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}
}

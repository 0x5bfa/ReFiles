// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Root AppModel that owns every window model in one Files process.
/// </summary>
public sealed class FilesApplicationModel : IAsyncDisposable
{
	private readonly IBrowsePaneFactory paneFactory;
	private readonly object syncRoot = new();
	private readonly object disposalLock = new();
	private readonly SemaphoreSlim mutationLock = new(1, 1);
	private readonly CancellationTokenSource lifetime = new();
	private readonly List<WindowModel> windows = [];
	private IReadOnlyList<WindowModel> windowSnapshot =
		Array.Empty<WindowModel>();
	private Guid activeWindowId;
	private Task? disposeTask;
	private volatile bool isDisposed;

	public FilesApplicationModel(IBrowsePaneFactory paneFactory)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);
		this.paneFactory = paneFactory;
	}

	public IReadOnlyList<WindowModel> Windows =>
		Volatile.Read(ref windowSnapshot);

	public WindowModel? ActiveWindow
	{
		get
		{
			lock (syncRoot)
			{
				return windows.FirstOrDefault(window => window.Id == activeWindowId);
			}
		}
	}

	public event EventHandler? StateChanged;

	public async ValueTask<WindowModel> CreateWindowAsync(
		BrowseLocation? initialLocation = null,
		CancellationToken cancellationToken = default)
	{
		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		WindowModel? window = null;
		try
		{
			EnsureActive();
			window = new WindowModel(paneFactory);
			await window
				.OpenTabAsync(initialLocation, linkedCancellation.Token)
				.ConfigureAwait(false);

			lock (syncRoot)
			{
				EnsureActive();
				windows.Add(window);
				window.StateChanged += OnWindowStateChanged;
				activeWindowId = window.Id;
				UpdateSnapshot();
			}

			var result = window;
			window = null;
			ModelEvent.Raise(this, StateChanged);
			return result;
		}
		catch (Exception creationError)
		{
			if (window is null)
			{
				throw;
			}

			try
			{
				await window.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException(
					"Window creation and cleanup failed.",
					creationError,
					cleanupError);
			}

			throw;
		}
		finally
		{
			mutationLock.Release();
		}
	}

	public async ValueTask<bool> CloseWindowAsync(
		Guid windowId,
		CancellationToken cancellationToken = default)
	{
		if (windowId == Guid.Empty)
		{
			throw new ArgumentException(
				"A window ID is required.",
				nameof(windowId));
		}

		using var linkedCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				lifetime.Token);
		await mutationLock
			.WaitAsync(linkedCancellation.Token)
			.ConfigureAwait(false);

		WindowModel? removed = null;
		try
		{
			EnsureActive();
			lock (syncRoot)
			{
				var index = windows.FindIndex(window => window.Id == windowId);
				if (index < 0)
				{
					return false;
				}

				removed = windows[index];
				windows.RemoveAt(index);
				removed.StateChanged -= OnWindowStateChanged;
				if (activeWindowId == windowId)
				{
					activeWindowId = windows.Count is 0
						? Guid.Empty
						: windows[Math.Min(index, windows.Count - 1)].Id;
				}

				UpdateSnapshot();
			}

			ModelEvent.Raise(this, StateChanged);
			var ownedWindow = removed!;
			removed = null;
			await ownedWindow.DisposeAsync().ConfigureAwait(false);
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

	public bool SetActiveWindow(Guid windowId)
	{
		if (windowId == Guid.Empty)
		{
			throw new ArgumentException(
				"A window ID is required.",
				nameof(windowId));
		}

		var changed = false;
		lock (syncRoot)
		{
			EnsureActive();
			if (!windows.Any(window => window.Id == windowId))
			{
				return false;
			}

			if (activeWindowId != windowId)
			{
				activeWindowId = windowId;
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
		WindowModel[] ownedWindows;
		try
		{
			lock (syncRoot)
			{
				ownedWindows = windows.ToArray();
				foreach (var window in ownedWindows)
				{
					window.StateChanged -= OnWindowStateChanged;
				}

				windows.Clear();
				activeWindowId = Guid.Empty;
				UpdateSnapshot();
			}
		}
		finally
		{
			mutationLock.Release();
		}

		List<Exception>? errors = null;
		foreach (var window in ownedWindows.Reverse())
		{
			try
			{
				await window.DisposeAsync().ConfigureAwait(false);
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
			throw new AggregateException(
				"One or more application windows could not be disposed.",
				errors);
		}
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(
			ref windowSnapshot,
			Array.AsReadOnly(windows.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}

	private void OnWindowStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}
}

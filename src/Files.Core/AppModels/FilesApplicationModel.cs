// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Root AppModel that owns every window model in one Files process.
/// </summary>
public sealed class FilesApplicationModel : IAsyncDisposable
{
	private readonly IBrowsePaneFactory _paneFactory;

	private readonly Lock _syncRoot = new();

	private readonly Lock _disposalLock = new();

	private readonly SemaphoreSlim _mutationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly List<WindowModel> _windows = [];

	private IReadOnlyList<WindowModel> _windowSnapshot = [];

	private Guid _activeWindowId;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	public IReadOnlyList<WindowModel> Windows => Volatile.Read(ref _windowSnapshot);

	public WindowModel? ActiveWindow
	{
		get
		{
			lock (_syncRoot)
			{
				return _windows.FirstOrDefault(window => window.Id == _activeWindowId);
			}
		}
	}

	public event EventHandler? StateChanged;

	public FilesApplicationModel(IBrowsePaneFactory paneFactory)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);

		_paneFactory = paneFactory;
	}

	public async ValueTask<WindowModel> CreateWindowAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		WindowModel? window = null;

		try
		{
			EnsureActive();
			window = new WindowModel(_paneFactory);
			await window.OpenTabAsync(initialLocation, linkedCancellation.Token).ConfigureAwait(false);

			lock (_syncRoot)
			{
				EnsureActive();
				_windows.Add(window);
				window.StateChanged += OnWindowStateChanged;
				_activeWindowId = window.Id;
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
				throw new AggregateException("Window creation and cleanup failed.", creationError, cleanupError);
			}

			throw;
		}
		finally
		{
			_mutationLock.Release();
		}
	}

	public async ValueTask<bool> CloseWindowAsync(Guid windowId, CancellationToken cancellationToken = default)
	{
		if (windowId == Guid.Empty)
		{
			throw new ArgumentException("A window ID is required.", nameof(windowId));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		WindowModel? removed = null;
		try
		{
			EnsureActive();
			lock (_syncRoot)
			{
				var index = _windows.FindIndex(window => window.Id == windowId);
				if (index < 0)
				{
					return false;
				}

				removed = _windows[index];
				_windows.RemoveAt(index);
				removed.StateChanged -= OnWindowStateChanged;

				if (_activeWindowId == windowId)
				{
					_activeWindowId = _windows.Count is 0
						? Guid.Empty
						: _windows[Math.Min(index, _windows.Count - 1)].Id;
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
			_mutationLock.Release();

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
			throw new ArgumentException("A window ID is required.", nameof(windowId));
		}

		var changed = false;

		lock (_syncRoot)
		{
			EnsureActive();

			if (!_windows.Any(window => window.Id == windowId))
			{
				return false;
			}

			if (_activeWindowId != windowId)
			{
				_activeWindowId = windowId;
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
		WindowModel[] ownedWindows;
		try
		{
			lock (_syncRoot)
			{
				ownedWindows = [.. _windows];
				foreach (var window in ownedWindows)
				{
					window.StateChanged -= OnWindowStateChanged;
				}

				_windows.Clear();
				_activeWindowId = Guid.Empty;
				UpdateSnapshot();
			}
		}
		finally
		{
			_mutationLock.Release();
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

		_mutationLock.Dispose();
		_lifetime.Dispose();
		GC.SuppressFinalize(this);

		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("One or more application windows could not be disposed.", errors);
		}
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _windowSnapshot, Array.AsReadOnly(_windows.ToArray()));
	}

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

	private void OnWindowStateChanged(object? sender, EventArgs args)
	{
		ModelEvent.Raise(this, StateChanged);
	}
}

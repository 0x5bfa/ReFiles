// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>
/// Owns the UI-independent window sessions in one Files process.
/// </summary>
public sealed class FilesApplicationSession : IAsyncDisposable
{
	private readonly IBrowsePaneSessionFactory _paneFactory;

	private readonly Lock _syncRoot = new();

	private readonly Lock _disposalLock = new();

	private readonly SemaphoreSlim _mutationLock = new(1, 1);

	private readonly CancellationTokenSource _lifetime = new();

	private readonly List<WindowSession> _windows = [];

	private IReadOnlyList<WindowSession> _windowSnapshot = [];

	private Guid _activeWindowId;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	/// <summary>Gets the owned window sessions.</summary>
	public IReadOnlyList<WindowSession> Windows => Volatile.Read(ref _windowSnapshot);

	/// <summary>Gets the window most recently activated by a host.</summary>
	public WindowSession? ActiveWindow
	{
		get
		{
			lock (_syncRoot)
			{
				return _windows.FirstOrDefault(window => window.Id == _activeWindowId);
			}
		}
	}

	/// <summary>Occurs when the window collection changes.</summary>
	public event EventHandler? WindowsChanged;

	/// <summary>Occurs when the active window changes.</summary>
	public event EventHandler? ActiveWindowChanged;

	/// <summary>Initializes an empty application shell session.</summary>
	/// <param name="paneFactory">The factory used for browse panes.</param>
	public FilesApplicationSession(IBrowsePaneSessionFactory paneFactory)
	{
		ArgumentNullException.ThrowIfNull(paneFactory);

		_paneFactory = paneFactory;
	}

	/// <summary>Creates and owns a window session.</summary>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created window session.</returns>
	public async ValueTask<WindowSession> CreateWindowAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default)
	{
		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		WindowSession? window = null;

		try
		{
			EnsureActive();
			window = new WindowSession(_paneFactory);
			await window.OpenTabAsync(initialLocation, linkedCancellation.Token).ConfigureAwait(false);

			lock (_syncRoot)
			{
				EnsureActive();
				_windows.Add(window);
				_activeWindowId = window.Id;
				UpdateSnapshot();
			}

			var result = window;
			window = null;
			SessionEvent.Raise(this, WindowsChanged);
			SessionEvent.Raise(this, ActiveWindowChanged);

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

	/// <summary>Closes and disposes a window session.</summary>
	/// <param name="windowId">The window identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the window existed.</returns>
	public async ValueTask<bool> CloseWindowAsync(Guid windowId, CancellationToken cancellationToken = default)
	{
		if (windowId == Guid.Empty)
		{
			throw new ArgumentException("A window ID is required.", nameof(windowId));
		}

		using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
		await _mutationLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

		WindowSession? removed = null;
		var activeWindowChanged = false;
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

				if (_activeWindowId == windowId)
				{
					activeWindowChanged = true;
					_activeWindowId = _windows.Count is 0
						? Guid.Empty
						: _windows[Math.Min(index, _windows.Count - 1)].Id;
				}

				UpdateSnapshot();
			}

			SessionEvent.Raise(this, WindowsChanged);
			if (activeWindowChanged)
			{
				SessionEvent.Raise(this, ActiveWindowChanged);
			}

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

	/// <summary>Records host activation for a window.</summary>
	/// <param name="windowId">The activated window identifier.</param>
	/// <returns><see langword="true"/> when the window exists.</returns>
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
			SessionEvent.Raise(this, ActiveWindowChanged);
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
		WindowSession[] ownedWindows;
		try
		{
			lock (_syncRoot)
			{
				ownedWindows = [.. _windows];
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
}

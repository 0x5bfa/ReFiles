// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.Foundation;

namespace Files.Core.Windows;

/// <summary>Coordinates a Windows Shell preview handler session.</summary>
public sealed class WindowsShellPreviewSession : IWindowsShellPreviewSession
{
	private readonly WindowsPreviewTarget _target;

	private readonly IWindowsPreviewHandlerController _controller;

	private readonly IWindowsShellScheduler _scheduler;

	private readonly Lock _syncRoot = new();

	private Task? _disposeTask;

	private WindowsShellPreviewSessionState _state =
		WindowsShellPreviewSessionState.Created;

	/// <summary>Gets the current session state.</summary>
	public WindowsShellPreviewSessionState State
	{
		get
		{
			lock (_syncRoot)
			{
				return _state;
			}
		}
	}

	internal WindowsShellPreviewSession(WindowsPreviewTarget target, IWindowsPreviewHandlerController controller, IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(controller);
		ArgumentNullException.ThrowIfNull(scheduler);

		_target = target;
		_controller = controller;
		_scheduler = scheduler;
	}

	/// <summary>Updates the preview bounds.</summary>
	/// <param name="bounds">The preview bounds.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();

		return new ValueTask(_scheduler.InvokeOperationAsync(() => {_controller.SetBounds(bounds); return true;}, cancellationToken));
	}

	/// <summary>Updates the preview colors.</summary>
	/// <param name="background">The background color.</param>
	/// <param name="foreground">The foreground color.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();

		return new ValueTask(_scheduler.InvokeOperationAsync(() => {_controller.SetTheme(background, foreground); return true;}, cancellationToken));
	}

	/// <summary>Gives keyboard focus to the preview handler.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public ValueTask SetFocusAsync(CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();

		return new ValueTask(_scheduler.InvokeOperationAsync(() => {_controller.SetFocus(); return true;}, cancellationToken));
	}

	/// <summary>Gets the window that currently has preview focus.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The focused window handle.</returns>
	public async ValueTask<HWND> QueryFocusAsync(CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();

		return await _scheduler.InvokeOperationAsync(() => _controller.QueryFocus(), cancellationToken).ConfigureAwait(false);
	}

	internal void TransitionTo(WindowsShellPreviewSessionState nextState)
	{
		lock (_syncRoot)
		{
			if (_state is WindowsShellPreviewSessionState.Disposed)
			{
				return;
			}

			_state = nextState;
		}
	}

	internal void CleanupControllerOnPreviewSta()
	{
		_controller.Dispose();
		lock (_syncRoot)
		{
			_state = WindowsShellPreviewSessionState.Disposed;
		}
	}

	/// <summary>Asynchronously disposes the preview session and its target.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_syncRoot)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_state = WindowsShellPreviewSessionState.Disposed;
			_disposeTask = DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		try
		{
			await _scheduler.InvokeOperationAsync(() => {_controller.Dispose(); return true;}).ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		try
		{
			await _target.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		GC.SuppressFinalize(this);
		if (errors.Count is 1)
		{
			throw errors[0];
		}

		if (errors.Count > 1)
		{
			throw new AggregateException("Preview handler and target cleanup failed.", errors);
		}
	}

	private void EnsurePreviewing()
	{
		lock (_syncRoot)
		{
			if (_state is not WindowsShellPreviewSessionState.Previewing)
			{
				throw new ObjectDisposedException(nameof(WindowsShellPreviewSession));
			}
		}
	}
}

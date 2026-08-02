// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;

namespace Files.Core.ItemFeatures.Previews;

public enum WindowsShellPreviewSessionState
{
	Created,
	Activating,
	Initialized,
	Previewing,
	Faulted,
	Disposed,
}

public sealed class WindowsShellPreviewSession : IWindowsShellPreviewSession
{
	private readonly WindowsPreviewTarget target;
	private readonly IWindowsPreviewHandlerController controller;
	private readonly IWindowsShellScheduler scheduler;
	private readonly object syncRoot = new();
	private Task? disposeTask;
	private WindowsShellPreviewSessionState state =
		WindowsShellPreviewSessionState.Created;

	internal WindowsShellPreviewSession(WindowsPreviewTarget target, IWindowsPreviewHandlerController controller, IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(controller);
		ArgumentNullException.ThrowIfNull(scheduler);

		this.target = target;
		this.controller = controller;
		this.scheduler = scheduler;
	}

	public WindowsShellPreviewSessionState State
	{
		get
		{
			lock (syncRoot)
			{
				return state;
			}
		}
	}

	public ValueTask SetBoundsAsync(WindowsPreviewBounds bounds, CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();
		return new ValueTask(scheduler.InvokeOperationAsync(() => {controller.SetBounds(bounds); return true;}, cancellationToken));
	}

	public ValueTask SetThemeAsync(WindowsPreviewColor background, WindowsPreviewColor foreground, CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();
		return new ValueTask(scheduler.InvokeOperationAsync(() => {controller.SetTheme(background, foreground); return true;}, cancellationToken));
	}

	public ValueTask SetFocusAsync(CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();
		return new ValueTask(scheduler.InvokeOperationAsync(() => {controller.SetFocus(); return true;}, cancellationToken));
	}

	public async ValueTask<nint> QueryFocusAsync(CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();
		return await scheduler
			.InvokeOperationAsync(() => controller.QueryFocus(), cancellationToken)
			.ConfigureAwait(false);
	}

	public ValueTask<bool> TryTranslateAcceleratorAsync(nint messagePointer, CancellationToken cancellationToken = default)
	{
		EnsurePreviewing();
		return new ValueTask<bool>(scheduler.InvokeOperationAsync(() => controller.TryTranslateAccelerator(messagePointer), cancellationToken));
	}

	internal void TransitionTo(WindowsShellPreviewSessionState nextState)
	{
		lock (syncRoot)
		{
			if (state is WindowsShellPreviewSessionState.Disposed)
			{
				return;
			}

			state = nextState;
		}
	}

	internal void CleanupControllerOnPreviewSta()
	{
		controller.Dispose();
		lock (syncRoot)
		{
			state = WindowsShellPreviewSessionState.Disposed;
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (syncRoot)
		{
			if (disposeTask is not null)
			{
				return new ValueTask(disposeTask);
			}

			state = WindowsShellPreviewSessionState.Disposed;
			disposeTask = DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		var errors = new List<Exception>();

		try
		{
			await scheduler
				.InvokeOperationAsync(() => {controller.Dispose(); return true;})
				.ConfigureAwait(false);
		}
		catch (Exception error)
		{
			errors.Add(error);
		}

		try
		{
			await target.DisposeAsync().ConfigureAwait(false);
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
		lock (syncRoot)
		{
			if (state is not WindowsShellPreviewSessionState.Previewing)
			{
				throw new ObjectDisposedException(nameof(WindowsShellPreviewSession));
			}
		}
	}
}

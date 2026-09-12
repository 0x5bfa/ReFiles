// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32.Foundation;
using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Data;
using Files.Core.Storage;

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

/// <summary>Creates Windows Shell preview sessions on a dedicated scheduler.</summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellPreviewSessionFactory : IWindowsShellPreviewSessionFactory
{
	private readonly IWindowsPreviewTargetResolver _targetResolver;
	private readonly IWindowsShellScheduler _scheduler;
	private readonly IWindowsPreviewHandlerControllerFactory _controllerFactory;
	private readonly IWindowsShellPreviewPolicy _policy;
	private readonly IWindowsPreviewHandlerRegistrationValidator _registrationValidator;

	/// <summary>Initializes a Windows Shell preview session factory.</summary>
	/// <param name="targetResolver">The preview target resolver.</param>
	/// <param name="dedicatedScheduler">The dedicated Shell scheduler.</param>
	/// <param name="controllerFactory">The native preview controller factory.</param>
	public WindowsShellPreviewSessionFactory(IWindowsPreviewTargetResolver targetResolver, IWindowsShellScheduler dedicatedScheduler, IWindowsPreviewHandlerControllerFactory controllerFactory)
		: this(targetResolver, dedicatedScheduler, controllerFactory, new WindowsPreviewAccessPolicy(), new WindowsPreviewHandlerRegistrationValidator())
	{
	}

	/// <summary>Initializes a Windows Shell preview session factory.</summary>
	/// <param name="targetResolver">The preview target resolver.</param>
	/// <param name="dedicatedScheduler">The dedicated Shell scheduler.</param>
	/// <param name="controllerFactory">The native preview controller factory.</param>
	/// <param name="policy">The policy to revalidate immediately before handler activation.</param>
	public WindowsShellPreviewSessionFactory(
		IWindowsPreviewTargetResolver targetResolver,
		IWindowsShellScheduler dedicatedScheduler,
		IWindowsPreviewHandlerControllerFactory controllerFactory,
		IWindowsShellPreviewPolicy policy)
		: this(targetResolver, dedicatedScheduler, controllerFactory, policy, new WindowsPreviewHandlerRegistrationValidator())
	{
	}

	internal WindowsShellPreviewSessionFactory(
		IWindowsPreviewTargetResolver targetResolver,
		IWindowsShellScheduler dedicatedScheduler,
		IWindowsPreviewHandlerControllerFactory controllerFactory,
		IWindowsShellPreviewPolicy policy,
		IWindowsPreviewHandlerRegistrationValidator registrationValidator)
	{
		ArgumentNullException.ThrowIfNull(targetResolver);
		ArgumentNullException.ThrowIfNull(dedicatedScheduler);
		ArgumentNullException.ThrowIfNull(controllerFactory);
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(registrationValidator);

		_targetResolver = targetResolver;
		_scheduler = dedicatedScheduler;
		_controllerFactory = controllerFactory;
		_policy = policy;
		_registrationValidator = registrationValidator;
	}

	/// <summary>Initializes a Windows Shell preview session factory from a workspace.</summary>
	/// <param name="workspace">The storage workspace.</param>
	/// <param name="dedicatedScheduler">The dedicated Shell scheduler.</param>
	public WindowsShellPreviewSessionFactory(IStorageWorkspace workspace, IWindowsShellScheduler dedicatedScheduler) : this(workspace, dedicatedScheduler, new WindowsPreviewAccessPolicy())
	{
	}

	/// <summary>Initializes a Windows Shell preview session factory from a workspace.</summary>
	/// <param name="workspace">The storage workspace.</param>
	/// <param name="dedicatedScheduler">The dedicated Shell scheduler.</param>
	/// <param name="policy">The policy to revalidate immediately before handler activation.</param>
	public WindowsShellPreviewSessionFactory(IStorageWorkspace workspace, IWindowsShellScheduler dedicatedScheduler, IWindowsShellPreviewPolicy policy)
		: this(new WindowsPreviewTargetResolver(workspace), dedicatedScheduler, new WindowsShellPreviewHandlerControllerFactory(), policy)
	{
	}

	/// <inheritdoc />
	public async ValueTask<IWindowsShellPreviewSession> CreateAsync(WindowsShellPreviewResult result, WindowsPreviewHost host, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(result);
		ArgumentNullException.ThrowIfNull(host);
		cancellationToken.ThrowIfCancellationRequested();

		WindowsPreviewTarget? target = null;
		try
		{
			target = await _targetResolver.ResolveAsync(result.Reference, cancellationToken).ConfigureAwait(false);
			if (target.Context is not { } context)
			{
				throw new WindowsShellPreviewBlockedException(PreviewBlockReason.DisabledByPolicy);
			}

			if (await _policy.GetBlockReasonAsync(result.Request, context, result.HandlerClsid, cancellationToken).ConfigureAwait(false) is { } blockReason)
			{
				throw new WindowsShellPreviewBlockedException(blockReason);
			}

			var session = await _scheduler.InvokeOperationAsync(() => CreateOnPreviewSta(result, host, target, context), cancellationToken).ConfigureAwait(false);

			target = null;
			if (cancellationToken.IsCancellationRequested)
			{
				await session.DisposeAsync().ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
			}

			return session;
		}
		catch (Exception creationError)
		{
			if (target is null)
			{
				throw;
			}

			try
			{
				await target.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Preview session creation and target cleanup failed.", creationError, cleanupError);
			}

			throw;
		}
	}

	private IWindowsShellPreviewSession CreateOnPreviewSta(WindowsShellPreviewResult result, WindowsPreviewHost host, WindowsPreviewTarget target, ItemContext context)
	{
		if (!_registrationValidator.IsCurrentHandler(context, result.HandlerClsid))
		{
			throw new WindowsShellPreviewBlockedException(PreviewBlockReason.DisabledByPolicy);
		}

		if (_policy.GetBlockReason(result.Request, context, result.HandlerClsid) is { } blockReason)
		{
			throw new WindowsShellPreviewBlockedException(blockReason);
		}

		var controller = _controllerFactory.Create(result.HandlerClsid);
		var session = new WindowsShellPreviewSession(target, controller, _scheduler);
		try
		{
			session.TransitionTo(WindowsShellPreviewSessionState.Activating);
			var windowsItem = target.Item;
			var initialized = windowsItem.FileSystemPath is { } fileSystemPath && controller.TryInitializeWithStream(fileSystemPath);

			if (!initialized)
			{
				initialized = controller.TryInitializeWithItem(windowsItem.ParsingName);
			}

			if (!initialized && windowsItem.FileSystemPath is { } fallbackPath)
			{
				initialized = controller.TryInitializeWithFile(fallbackPath);
			}

			if (!initialized)
			{
				throw new NotSupportedException("The preview handler does not support any initialization contract.");
			}

			controller.SetSite(host.WindowHandle, host.AcceleratorForwarder);
			session.TransitionTo(WindowsShellPreviewSessionState.Initialized);
			controller.SetWindow(host.WindowHandle, default);
			controller.ApplySystemVisuals();
			controller.DoPreview();
			session.TransitionTo(WindowsShellPreviewSessionState.Previewing);

			return session;
		}
		catch (Exception activationError)
		{
			session.TransitionTo(WindowsShellPreviewSessionState.Faulted);
			try
			{
				session.CleanupControllerOnPreviewSta();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Preview handler activation and cleanup failed.", activationError, cleanupError);
			}

			throw;
		}
	}
}

/// <summary>Loads Windows Shell preview handler results for supported files.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShellPreviewLoader : IPreviewLoader
{
	private readonly IWindowsPreviewHandlerResolver _handlerResolver;
	private readonly IWindowsShellPreviewPolicy _policy;

	/// <summary>Initializes a Windows Shell preview loader.</summary>
	/// <param name="handlerResolver">The preview handler resolver.</param>
	/// <param name="policy">The Shell preview policy.</param>
	public WindowsShellPreviewLoader(IWindowsPreviewHandlerResolver handlerResolver, IWindowsShellPreviewPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(handlerResolver);
		ArgumentNullException.ThrowIfNull(policy);

		_handlerResolver = handlerResolver;
		_policy = policy;
	}

	/// <summary>Determines whether Windows Shell preview applies to an item.</summary>
	/// <param name="context">The item context.</param>
	/// <returns><see langword="true"/> when the item is a Windows-backed file.</returns>
	public bool CanLoad(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.CoreModel is IWindowsStorable && context.CoreModel is IFile;
	}

	/// <summary>Resolves a Windows Shell preview handler for an item.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A Shell preview result, a blocked result, or <see langword="null"/> when unsupported.</returns>
	public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		if (!CanLoad(context))
		{
			return null;
		}

		var handlerClsid = await _handlerResolver.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		if (handlerClsid is null)
		{
			return null;
		}

		var blockReason = await _policy.GetBlockReasonAsync(request, context, handlerClsid.Value, cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		return blockReason is not null ? new BlockedPreviewResult(blockReason.Value) : new WindowsShellPreviewResult(context.Reference, handlerClsid.Value, request);
	}
}

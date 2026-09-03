// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Data;
using Files.Core.Storage.Windows;

namespace Files.Core.Capabilities.Previews;

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
	public WindowsShellPreviewSessionFactory(IStorageWorkspace workspace, IWindowsShellScheduler dedicatedScheduler)
		: this(workspace, dedicatedScheduler, new WindowsPreviewAccessPolicy())
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
			var initialized = windowsItem.FileSystemPath is { } fileSystemPath
				&& controller.TryInitializeWithStream(fileSystemPath);

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

			controller.SetSite();
			session.TransitionTo(WindowsShellPreviewSessionState.Initialized);
			controller.SetWindow(host.WindowHandle, host.Bounds);
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

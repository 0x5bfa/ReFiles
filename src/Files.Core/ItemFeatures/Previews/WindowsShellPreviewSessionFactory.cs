// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Data;
using Files.Core.Storage.Windows;

namespace Files.Core.ItemFeatures.Previews;

[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellPreviewSessionFactory : IWindowsShellPreviewSessionFactory
{
	private readonly IWindowsPreviewTargetResolver targetResolver;
	private readonly IWindowsShellScheduler scheduler;
	private readonly IWindowsPreviewHandlerControllerFactory controllerFactory;

	public WindowsShellPreviewSessionFactory(
		IWindowsPreviewTargetResolver targetResolver,
		IWindowsShellScheduler dedicatedScheduler,
		IWindowsPreviewHandlerControllerFactory controllerFactory)
	{
		ArgumentNullException.ThrowIfNull(targetResolver);
		ArgumentNullException.ThrowIfNull(dedicatedScheduler);
		ArgumentNullException.ThrowIfNull(controllerFactory);

		this.targetResolver = targetResolver;
		scheduler = dedicatedScheduler;
		this.controllerFactory = controllerFactory;
	}

	public WindowsShellPreviewSessionFactory(IFilesDataRoot dataRoot, IWindowsShellScheduler dedicatedScheduler)
		: this(new WindowsPreviewTargetResolver(dataRoot), dedicatedScheduler, new WindowsShellPreviewHandlerControllerFactory())
	{
	}

	public async ValueTask<IWindowsShellPreviewSession> CreateAsync(
		WindowsShellPreviewResult result,
		WindowsPreviewHost host,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(result);
		ArgumentNullException.ThrowIfNull(host);
		cancellationToken.ThrowIfCancellationRequested();

		WindowsPreviewTarget? target = null;
		try
		{
			target = await targetResolver
				.ResolveAsync(result.Reference, cancellationToken)
				.ConfigureAwait(false);

			var session = await scheduler
				.InvokeOperationAsync(() => CreateOnPreviewSta(result, host, target), cancellationToken)
				.ConfigureAwait(false);

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

	private IWindowsShellPreviewSession CreateOnPreviewSta(WindowsShellPreviewResult result, WindowsPreviewHost host, WindowsPreviewTarget target)
	{
		var controller = controllerFactory.Create(result.HandlerClsid);
		var session = new WindowsShellPreviewSession(target, controller, scheduler);
		try
		{
			session.TransitionTo(WindowsShellPreviewSessionState.Activating);
			controller.SetSite();

			var windowsItem = target.Item;
			var initialized = windowsItem.FileSystemPath is { } fileSystemPath
				&& controller.TryInitializeWithStream(fileSystemPath);

			if (!initialized)
			{
				initialized = controller.TryInitializeWithItem(windowsItem.ParsingName);
			}

			if (!initialized
				&& windowsItem.FileSystemPath is { } fallbackPath)
			{
				initialized = controller.TryInitializeWithFile(fallbackPath);
			}

			if (!initialized)
			{
				throw new NotSupportedException("The preview handler does not support any initialization contract.");
			}

			session.TransitionTo(WindowsShellPreviewSessionState.Initialized);
			controller.SetWindow(host.WindowHandle, host.Bounds);
			controller.SetBounds(host.Bounds);
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

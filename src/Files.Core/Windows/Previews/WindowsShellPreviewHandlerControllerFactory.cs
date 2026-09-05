// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;

namespace Files.Core.Windows;

/// <summary>Creates controllers for Windows Shell preview handlers.</summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellPreviewHandlerControllerFactory : IWindowsPreviewHandlerControllerFactory
{
	private readonly IWindowsPreviewHandlerActivationPolicy _activationPolicy;
	private readonly IWindowsPreviewHandlerIsolationPolicy _isolationPolicy;

	/// <summary>Initializes a controller factory with the local-server policy.</summary>
	public WindowsShellPreviewHandlerControllerFactory()
		: this(new LocalServerWindowsPreviewHandlerActivationPolicy(), new WindowsPreviewHandlerIsolationPolicy())
	{
	}

	/// <summary>Initializes a controller factory.</summary>
	/// <param name="activationPolicy">The activation policy.</param>
	public WindowsShellPreviewHandlerControllerFactory(IWindowsPreviewHandlerActivationPolicy activationPolicy)
		: this(activationPolicy, new WindowsPreviewHandlerIsolationPolicy())
	{
	}

	internal WindowsShellPreviewHandlerControllerFactory(IWindowsPreviewHandlerActivationPolicy activationPolicy, IWindowsPreviewHandlerIsolationPolicy isolationPolicy)
	{
		ArgumentNullException.ThrowIfNull(activationPolicy);
		ArgumentNullException.ThrowIfNull(isolationPolicy);

		_activationPolicy = activationPolicy;
		_isolationPolicy = isolationPolicy;
	}

	/// <summary>Creates a controller for a preview handler CLSID.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The created controller.</returns>
	public IWindowsPreviewHandlerController Create(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		var activationContext = _activationPolicy.GetContext(handlerClsid);
		var requiredContext = WindowsPreviewHandlerActivationContext.LocalServer | WindowsPreviewHandlerActivationContext.EnableCloaking;
		if (activationContext != requiredContext)
		{
			throw new InvalidOperationException("Preview handlers must be activated through a cloaked local server.");
		}

		var useLowIntegrity = _isolationPolicy.RequiresLowIntegrity(handlerClsid);
		var nativeContext = useLowIntegrity ? activationContext : WindowsPreviewHandlerActivationContext.LocalServer;

		return WindowsShellPreviewHandlerController.Create(handlerClsid, (uint)nativeContext, useLowIntegrity);
	}
}

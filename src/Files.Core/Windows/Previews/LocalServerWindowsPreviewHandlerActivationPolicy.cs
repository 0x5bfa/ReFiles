// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Activates preview handlers through a local server.</summary>
public sealed class LocalServerWindowsPreviewHandlerActivationPolicy
    : IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the local-server activation context.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>A local-server activation context with cloaking enabled.</returns>
	public WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return WindowsPreviewHandlerActivationContext.LocalServer | WindowsPreviewHandlerActivationContext.EnableCloaking;
	}
}

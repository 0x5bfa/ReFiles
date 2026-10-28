// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Storage.Windows;

/// <summary>Activates preview handlers using their registered in-process or local-server COM class.</summary>
public sealed class LocalServerWindowsPreviewHandlerActivationPolicy : IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the local-server activation context.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>An activation context that supports both registered server types.</returns>
	public WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return WindowsPreviewHandlerActivationContext.ShellDefault;
	}
}

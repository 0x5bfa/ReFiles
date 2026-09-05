// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Chooses the activation context for a preview handler.</summary>
public interface IWindowsPreviewHandlerActivationPolicy
{
	/// <summary>Gets the activation context for a handler.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The permitted activation context.</returns>
	WindowsPreviewHandlerActivationContext GetContext(Guid handlerClsid);
}

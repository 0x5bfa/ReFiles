// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Creates Windows preview handler controllers.</summary>
public interface IWindowsPreviewHandlerControllerFactory
{
	/// <summary>Creates a controller for a preview handler CLSID.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The created controller.</returns>
	IWindowsPreviewHandlerController Create(Guid handlerClsid);
}

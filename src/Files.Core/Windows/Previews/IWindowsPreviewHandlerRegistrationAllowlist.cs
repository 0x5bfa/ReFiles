// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Determines whether a Windows preview handler is registered for use by the Shell preview host.</summary>
public interface IWindowsPreviewHandlerRegistrationAllowlist
{
	/// <summary>Determines whether the specified preview handler is registered.</summary>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see langword="true"/> when the handler is registered; otherwise, <see langword="false"/>.</returns>
	bool IsRegistered(Guid handlerClsid);
}

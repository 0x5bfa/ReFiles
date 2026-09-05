// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Looks up Windows preview handlers by file extension.</summary>
public interface IWindowsPreviewHandlerAssociation
{
	/// <summary>Gets the preview handler CLSID for a normalized extension.</summary>
	/// <param name="normalizedExtension">The normalized extension, including its leading period.</param>
	/// <returns>The handler CLSID string, or <see langword="null"/> when none is registered.</returns>
	string? QueryPreviewHandler(string normalizedExtension);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Looks up Windows preview handlers by file extension.</summary>
public interface IWindowsPreviewHandlerAssociation
{
	/// <summary>Gets the preview handler CLSID for a normalized extension.</summary>
	/// <param name="normalizedExtension">The normalized extension, including its leading period.</param>
	/// <returns>The handler CLSID string, or <see langword="null"/> when none is registered.</returns>
	string? QueryPreviewHandler(string normalizedExtension);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Changes;

namespace Files.Core.Windows;

/// <summary>
/// Creates a Windows Shell change source for a Windows folder.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class FolderChangeSourceFactory : ICapabilityFactory<IFolderChangeSource>
{
	/// <summary>Creates a Windows folder change source when the context represents a Windows folder.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The change source, or <see langword="null"/> when the context is not supported.</returns>
	public IFolderChangeSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource source && context.CoreModel is WindowsFolder folder
			? new WindowsFolderChangeSource(source, folder.Locator)
			: null;
	}
}

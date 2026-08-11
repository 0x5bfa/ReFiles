// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Storage.Windows;

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Creates a Windows Shell change source for a Windows folder.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class FolderChangeSourceFactory : IItemFeatureFactory<IFolderChangeSource>
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

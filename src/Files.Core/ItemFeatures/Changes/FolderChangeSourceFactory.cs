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
	public IFolderChangeSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource source && context.CoreModel is WindowsFolder folder
			? new WindowsFolderChangeSource(source, folder.Locator)
			: null;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Thumbnails;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Creates a thumbnail source for items resolved by the Windows storage source.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsThumbnailSourceFactory : IItemFeatureFactory<IThumbnailSource>
{
	private readonly WindowsShellThumbnailBackend _backend;

	public WindowsThumbnailSourceFactory(WindowsShellThumbnailBackend backend)
	{
		ArgumentNullException.ThrowIfNull(backend);

		_backend = backend;
	}

	public IThumbnailSource? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Source is not WindowsStorageSource source || context.CoreModel is not WindowsStorable storable)
		{
			return null;
		}

		return new WindowsShellThumbnailSource(source.ShellItemResolver, _backend, storable.Locator);
	}
}

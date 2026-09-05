// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;

namespace Files.Core.Windows;

/// <summary>
/// Creates a thumbnail source for items resolved by the Windows storage source.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsThumbnailSourceFactory : ICapabilityFactory<IThumbnailSource>
{
	private readonly WindowsShellThumbnailBackend _backend;

	/// <summary>Initializes a Windows thumbnail source factory.</summary>
	/// <param name="backend">The Windows thumbnail backend.</param>
	public WindowsThumbnailSourceFactory(WindowsShellThumbnailBackend backend)
	{
		ArgumentNullException.ThrowIfNull(backend);

		_backend = backend;
	}

	/// <summary>Creates a thumbnail source for a Windows item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns>The thumbnail source, or <see langword="null"/> when unsupported.</returns>
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

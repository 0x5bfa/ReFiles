// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Creates a thumbnail source for items resolved by the Windows storage source.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsThumbnailSourceFactory : ICapabilityFactory<IThumbnailSource>
{
	private readonly ConcurrentDictionary<WindowsShellThumbnailSource.SystemIconCacheKey, byte[]> _systemIconCache = new();
	private readonly ILoggerFactory _loggerFactory;

	/// <summary>Initializes a Windows thumbnail source factory.</summary>
	/// <param name="loggerFactory">The optional borrowed logger factory.</param>
	public WindowsThumbnailSourceFactory(ILoggerFactory? loggerFactory = null)
	{
		_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
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

		return new WindowsShellThumbnailSource(source.Scheduler, storable.Locator, _systemIconCache, _loggerFactory.CreateLogger<WindowsShellThumbnailSource>());
	}
}

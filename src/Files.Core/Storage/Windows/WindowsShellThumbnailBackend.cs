// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Capabilities.Thumbnails;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Retrieves Windows Shell thumbnails and materializes them as PNG bytes.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsShellThumbnailBackend
{
	private const SIIGBF ThumbnailFlags =
		SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK;

	private const SIIGBF IconFlags =
		SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK;

	private const WTS_FLAGS ThumbnailCacheOnlyFlags =
		WTS_FLAGS.WTS_INCACHEONLY | WTS_FLAGS.WTS_SCALETOREQUESTEDSIZE;

	private const WTS_FLAGS ThumbnailCacheExtractFlags =
		WTS_FLAGS.WTS_EXTRACT
		| WTS_FLAGS.WTS_SCALETOREQUESTEDSIZE
		| WTS_FLAGS.WTS_SCALEUP;

	// IExtractImage flags are not present in the Windows metadata used by
	// CsWin32, so keep the named values at this interop boundary.
	private const uint ExtractImageCacheFlag = 0x00000002;

	private const uint ExtractImageQualityFlag = 0x00000200;

	private const uint ExtractIconForShellFlag = 0x00000002;

	// SIOM_* and SHIL_* are SDK macros rather than generated enum members.
	private const uint OverlayIndexFlag = 0x00000001;

	private const uint IconIndexFlag = 0x00000002;

	private const int ShellImageListLarge = 0;

	private const int ShellImageListSmall = 1;

	private const int ShellImageListExtraLarge = 2;

	private const int ShellImageListJumbo = 4;

	private const int MaximumThumbnailSize = 1024;

	private static readonly Guid _clsidLocalThumbnailCache =
		new("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");

	private static readonly Guid _clsidCfsIconOverlayManager =
		new("63B51F81-C868-11D0-999C-00C04FD655E1");

	internal unsafe WindowsThumbnailPayload? GetThumbnail(IShellItem shellItem, WindowsItemLocator locator, ThumbnailRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var requestedSize = request.RequestedPixelSize;
		WindowsThumbnailPayload? payload = request.Mode switch
		{
			ThumbnailMode.Icon => TryGetIcon(shellItem, locator, requestedSize, cancellationToken),
			ThumbnailMode.Content => TryGetContent(shellItem, locator, requestedSize, cancellationToken),
			ThumbnailMode.PreferContent => TryGetContent(shellItem, locator, requestedSize, cancellationToken)
				?? TryGetIcon(shellItem, locator, requestedSize, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(request.Mode)),
		};

		return payload is null
			? null
			: CompleteWithOverlay(payload, locator, requestedSize, cancellationToken);
	}

	private static WindowsThumbnailPayload? TryGetContent(IShellItem shellItem, WindowsItemLocator locator, int requestedSize, CancellationToken cancellationToken)
	{
		var payload = TryGetThumbnailCache(shellItem, requestedSize, ThumbnailCacheOnlyFlags, cancellationToken);
		if (payload is not null)
		{
			return payload;
		}

		var imageFactory = TryCreateImageFactory(locator);
		if (imageFactory is not null)
		{
			payload = TryGetImage(imageFactory, requestedSize, ThumbnailFlags | SIIGBF.SIIGBF_INCACHEONLY, isFallback: false, cancellationToken);
			if (payload is not null)
			{
				return payload;
			}
		}

		payload = TryGetThumbnailCache(shellItem, requestedSize, ThumbnailCacheExtractFlags, cancellationToken);
		if (payload is not null)
		{
			return payload;
		}

		if (imageFactory is not null)
		{
			payload = TryGetImage(imageFactory, requestedSize, ThumbnailFlags, isFallback: false, cancellationToken);
			if (payload is not null)
			{
				return payload;
			}
		}

		return TryExtractLegacyThumbnail(locator, requestedSize, cancellationToken);
	}

	private static WindowsThumbnailPayload? TryGetIcon(IShellItem shellItem, WindowsItemLocator locator, int requestedSize, CancellationToken cancellationToken)
	{
		var imageFactory = TryCreateImageFactory(locator);
		if (imageFactory is not null)
		{
			var payload = TryGetImage(imageFactory, requestedSize, IconFlags | SIIGBF.SIIGBF_INCACHEONLY, isFallback: true, cancellationToken);
			if (payload is not null)
			{
				return payload;
			}

			payload = TryGetImage(imageFactory, requestedSize, IconFlags, isFallback: true, cancellationToken);
			if (payload is not null)
			{
				return payload;
			}
		}

		return TryRenderSystemIcon(locator, requestedSize, cancellationToken);
	}

	private static unsafe IShellItemImageFactory? TryCreateImageFactory(WindowsItemLocator locator)
	{
		var result = PInvoke.SHCreateItemFromParsingName(locator.ParsingName, null, out IShellItemImageFactory imageFactory);
		if (result.Succeeded)
		{
			return imageFactory;
		}

		if (locator.AbsolutePidl.IsEmpty)
		{
			return null;
		}

		fixed (byte* pidlBytes = locator.AbsolutePidl.Span)
		{
			var pidlResult = PInvoke.SHCreateItemFromIDList(in *(ITEMIDLIST*)pidlBytes, out imageFactory);

			return pidlResult.Succeeded ? imageFactory : null;
		}
	}

	private static WindowsThumbnailPayload? TryGetThumbnailCache(IShellItem shellItem, int requestedSize, WTS_FLAGS flags, CancellationToken cancellationToken)
	{
		if (requestedSize <= 0 || requestedSize > MaximumThumbnailSize)
		{
			return null;
		}

		var createResult = PInvoke.CoCreateInstance(_clsidLocalThumbnailCache, null, CLSCTX.CLSCTX_INPROC_SERVER, out global::Windows.Win32.UI.Shell.IThumbnailCache thumbnailCache);
		if (createResult.Failed || thumbnailCache is null)
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();

		var result = thumbnailCache.GetThumbnail(shellItem, (uint)requestedSize, flags, out ISharedBitmap sharedBitmap, out _, out _);
		if (result.Failed || sharedBitmap is null)
		{
			return null;
		}

		var formatResult = sharedBitmap.GetFormat(out var alphaType);
		if (formatResult.Failed)
		{
			return null;
		}

		var bitmapResult = sharedBitmap.GetSharedBitmap(out var sharedBitmapHandle);
		using (sharedBitmapHandle)
		{
			if (bitmapResult.Failed || sharedBitmapHandle.IsInvalid)
			{
				return null;
			}

			var content = WindowsThumbnailRenderer.EncodeHBitmap(sharedBitmapHandle, cancellationToken, alphaType is WTS_ALPHATYPE.WTSAT_RGB);

			return content is null
				? null
				: new WindowsThumbnailPayload(content, "image/png", IsFallback: false);
		}
	}

	private static WindowsThumbnailPayload? TryGetImage(IShellItemImageFactory imageFactory, int requestedSize, SIIGBF flags, bool isFallback, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var result = imageFactory.GetImage(new SIZE(requestedSize, requestedSize), flags, out var bitmap);
		using (bitmap)
		{
			if (result.Failed || bitmap.IsInvalid)
			{
				return null;
			}

			var content = WindowsThumbnailRenderer.EncodeHBitmap(bitmap, cancellationToken);

			return content is null
				? null
				: new WindowsThumbnailPayload(content, "image/png", isFallback);
		}
	}

	private static unsafe WindowsThumbnailPayload? TryExtractLegacyThumbnail(WindowsItemLocator locator, int requestedSize, CancellationToken cancellationToken)
	{
		if (!TryGetShellChildInterface<IExtractImage>(locator, out var extractImage, out var pidl))
		{
			return null;
		}

		try
		{
			Span<char> pathBuffer = stackalloc char[260];
			var priority = 0U;
			var flags = ExtractImageCacheFlag | ExtractImageQualityFlag;
			var locationResult = extractImage.GetLocation(pathBuffer, ref priority, new SIZE(requestedSize, requestedSize), 32, ref flags);
			if (locationResult.Failed)
			{
				return null;
			}

			cancellationToken.ThrowIfCancellationRequested();

			var extractResult = extractImage.Extract(out var bitmap);
			using (bitmap)
			{
				if (extractResult.Failed || bitmap.IsInvalid)
				{
					return null;
				}

				var content = WindowsThumbnailRenderer.EncodeHBitmap(bitmap, cancellationToken);

				return content is null
					? null
					: new WindowsThumbnailPayload(content, "image/png", IsFallback: false);
			}
		}
		finally
		{
			PInvoke.CoTaskMemFree(pidl);
		}
	}

	private static unsafe WindowsThumbnailPayload? TryRenderSystemIcon(WindowsItemLocator locator, int requestedSize, CancellationToken cancellationToken)
	{
		if (!TryGetShellChildInterface<IExtractIconW>(locator, out var extractIcon, out var pidl))
		{
			return null;
		}

		try
		{
			Span<char> iconPathBuffer = stackalloc char[260];
			var locationResult = extractIcon.GetIconLocation(ExtractIconForShellFlag, iconPathBuffer, out var iconIndex, out _);
			if (locationResult.Failed)
			{
				return null;
			}

			var terminator = iconPathBuffer.IndexOf('\0');
			var iconPath = new string(terminator >= 0 ? iconPathBuffer[..terminator] : iconPathBuffer);
			if (string.IsNullOrWhiteSpace(iconPath))
			{
				return null;
			}

			var packedSize = checked((uint)requestedSize | ((uint)requestedSize << 16));
			var extractResult = extractIcon.Extract(iconPath, unchecked((uint)iconIndex), out var largeIcon, out var smallIcon, packedSize);
			using (largeIcon)
			using (smallIcon)
			{
				if (extractResult.Failed)
				{
					return null;
				}

				if (!largeIcon.IsInvalid && !smallIcon.IsInvalid && largeIcon.DangerousGetHandle() == smallIcon.DangerousGetHandle())
				{
					smallIcon.SetHandleAsInvalid();
				}

				var icon = !largeIcon.IsInvalid
					? largeIcon
					: smallIcon;
				if (icon.IsInvalid)
				{
					return null;
				}

				var content = WindowsThumbnailRenderer.EncodeHIcon(icon, requestedSize, cancellationToken);

				return content is null
					? null
					: new WindowsThumbnailPayload(content, "image/png", IsFallback: true);
			}
		}
		finally
		{
			PInvoke.CoTaskMemFree(pidl);
		}
	}

	private static unsafe WindowsThumbnailPayload CompleteWithOverlay(WindowsThumbnailPayload payload, WindowsItemLocator locator, int requestedSize, CancellationToken cancellationToken)
	{
		if (!TryGetOverlayIcon(locator, requestedSize, out var overlayIcon) || overlayIcon is null)
		{
			return payload;
		}

		using (overlayIcon)
		{
			return WindowsThumbnailRenderer.TryCompositeOverlay(payload.Content, overlayIcon, out var compositedContent, cancellationToken)
				? payload with { Content = compositedContent }
				: payload;
		}
	}

	private static unsafe bool TryGetOverlayIcon(WindowsItemLocator locator, int requestedSize, out DestroyIconSafeHandle? overlayIcon)
	{
		overlayIcon = null;
		var createResult = PInvoke.CoCreateInstance(_clsidCfsIconOverlayManager, null, CLSCTX.CLSCTX_INPROC_SERVER, out IShellIconOverlayManager manager);
		if (createResult.Failed || manager is null)
		{
			return false;
		}

		uint attributes = 0;
		var fileAttributes = PInvoke.GetFileAttributes(locator.ParsingName);
		if (fileAttributes != PInvoke.INVALID_FILE_ATTRIBUTES)
		{
			attributes = fileAttributes;
		}

		var imageResult = manager.GetFileOverlayInfo(locator.ParsingName, attributes, out _, IconIndexFlag);
		if (imageResult.Failed)
		{
			return false;
		}

		var overlayResult = manager.GetFileOverlayInfo(locator.ParsingName, attributes, out var overlayIndex, OverlayIndexFlag);
		if (overlayResult.Failed || overlayIndex <= 0)
		{
			return false;
		}

		var imageListId = requestedSize switch
		{
			<= 16 => ShellImageListSmall,
			<= 32 => ShellImageListLarge,
			<= 48 => ShellImageListExtraLarge,
			_ => ShellImageListJumbo,
		};
		var imageListResult = PInvoke.SHGetImageList<IImageList>(imageListId, out var imageList);
		if (imageListResult.Failed || imageList is null)
		{
			return false;
		}

		var overlayImageResult = imageList.GetOverlayImage(overlayIndex, out var overlayImageIndex);
		if (overlayImageResult.Failed)
		{
			return false;
		}

		var iconResult = UI_Controls_IImageList_Extensions.GetIcon(imageList, overlayImageIndex, 0, out var icon);
		if (iconResult.Failed || icon.IsInvalid)
		{
			icon.Dispose();

			return false;
		}

		overlayIcon = icon;

		return true;
	}

	private static unsafe bool TryGetShellChildInterface<T>(WindowsItemLocator locator, out T result, out ITEMIDLIST* pidl)
		where T : class
	{
		result = null!;
		pidl = null;
		var parseResult = PInvoke.SHParseDisplayName(locator.ParsingName, null, out pidl, 0, out _);
		if (parseResult.Failed || pidl is null)
		{
			if (pidl is not null)
			{
				PInvoke.CoTaskMemFree(pidl);
				pidl = null;
			}

			return false;
		}

		var shellFolderId = typeof(IShellFolder).GUID;
		var bindResult = PInvoke.SHBindToParent(in *pidl, in shellFolderId, out object folderObject, out ITEMIDLIST* childPidl);
		if (bindResult.Failed || folderObject is not IShellFolder folder)
		{
			PInvoke.CoTaskMemFree(pidl);
			pidl = null;

			return false;
		}

		var childArray = childPidl;
		var resultInterface = typeof(T).GUID;
		var uiResult = folder.GetUIObjectOf(default, 1, &childArray, resultInterface, out object interfaceObject);
		if (uiResult.Failed || interfaceObject is not T typedResult)
		{
			PInvoke.CoTaskMemFree(pidl);
			pidl = null;

			return false;
		}

		result = typedResult;

		return true;
	}
}

internal sealed record WindowsThumbnailPayload(byte[] Content, string ContentType, bool IsFallback);

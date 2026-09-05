// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Capabilities.Thumbnails;

namespace Files.Core.Windows;

internal sealed record WindowsThumbnailPayload(
	byte[] Content,
	string ContentType,
	bool IsFallback,
	ThumbnailContentFormat Format = ThumbnailContentFormat.EncodedImage,
	int PixelWidth = 0,
	int PixelHeight = 0,
	bool IncludesOverlay = false);

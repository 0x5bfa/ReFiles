// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Transfers a materialized thumbnail payload across the process boundary.</summary>
public struct ThumbnailCacheEntryData
{
	/// <summary>Whether the cache contains an entry.</summary>
	public bool HasValue;
	/// <summary>The thumbnail bytes as Base64 text.</summary>
	public string ContentBase64;
	/// <summary>The MIME type or media description.</summary>
	public string ContentType;
	/// <summary>Whether the entry is a fallback representation.</summary>
	public bool IsFallback;
	/// <summary>The content representation.</summary>
	public int Format;
	/// <summary>The pixel width for raw content, or zero for encoded images.</summary>
	public int PixelWidth;
	/// <summary>The pixel height for raw content, or zero for encoded images.</summary>
	public int PixelHeight;
}

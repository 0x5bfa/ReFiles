// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Thumbnails;

/// <summary>
/// Contains an immutable thumbnail payload suitable for a shared cache.
/// </summary>
public sealed class ThumbnailCacheEntry
{
	private readonly byte[] _content;

	/// <summary>Gets the immutable thumbnail bytes.</summary>
	public ReadOnlyMemory<byte> Content => _content;

	/// <summary>Gets the MIME type of the thumbnail content.</summary>
	public string ContentType { get; }

	/// <summary>Gets a value indicating whether the thumbnail is a fallback representation.</summary>
	public bool IsFallback { get; }

	/// <summary>Gets the thumbnail content representation.</summary>
	public ThumbnailContentFormat Format { get; }

	/// <summary>Gets the pixel width for raw pixel content, or zero for encoded images.</summary>
	public int PixelWidth { get; }

	/// <summary>Gets the pixel height for raw pixel content, or zero for encoded images.</summary>
	public int PixelHeight { get; }

	/// <summary>Initializes a thumbnail cache entry.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of the content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
	public ThumbnailCacheEntry(byte[] content, string contentType, bool isFallback = false) : this(content, contentType, isFallback, ThumbnailContentFormat.EncodedImage, 0, 0)
	{
	}

	/// <summary>Initializes a thumbnail cache entry with an explicit content representation.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of encoded content, or a descriptive media type for raw content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
	/// <param name="format">The thumbnail content representation.</param>
	/// <param name="pixelWidth">The pixel width for raw content.</param>
	/// <param name="pixelHeight">The pixel height for raw content.</param>
	public ThumbnailCacheEntry(byte[] content, string contentType, bool isFallback, ThumbnailContentFormat format, int pixelWidth, int pixelHeight)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (content.Length is 0)
		{
			throw new ArgumentException("Thumbnail content cannot be empty.", nameof(content));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		var result = new ThumbnailResult(content, contentType, isFallback, format, pixelWidth, pixelHeight);
		_content = result.Content.ToArray();
		ContentType = contentType;
		IsFallback = isFallback;
		Format = format;
		PixelWidth = pixelWidth;
		PixelHeight = pixelHeight;
	}

	internal ThumbnailResult CreateResult()
	{
		return new ThumbnailResult(Content, ContentType, IsFallback, Format, PixelWidth, PixelHeight);
	}
}

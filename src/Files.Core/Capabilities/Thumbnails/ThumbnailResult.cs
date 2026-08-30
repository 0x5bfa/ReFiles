// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Thumbnails;

/// <summary>Identifies how thumbnail content is represented.</summary>
public enum ThumbnailContentFormat
{
	/// <summary>The content is an encoded image described by its MIME type.</summary>
	EncodedImage,

	/// <summary>The content is a tightly packed BGRA8 pixel buffer.</summary>
	Bgra8,
}

/// <summary>Contains thumbnail content and its presentation metadata.</summary>
public sealed record ThumbnailResult
{
	/// <summary>Gets the thumbnail bytes.</summary>
	public ReadOnlyMemory<byte> Content { get; }

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

	/// <summary>Initializes a thumbnail result.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of the content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
	public ThumbnailResult(ReadOnlyMemory<byte> content, string contentType, bool isFallback) : this(content, contentType, isFallback, ThumbnailContentFormat.EncodedImage, 0, 0)
	{
	}

	/// <summary>Initializes a thumbnail result with an explicit content representation.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of encoded content, or a descriptive media type for raw content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
	/// <param name="format">The thumbnail content representation.</param>
	/// <param name="pixelWidth">The pixel width for raw content.</param>
	/// <param name="pixelHeight">The pixel height for raw content.</param>
	public ThumbnailResult(ReadOnlyMemory<byte> content, string contentType, bool isFallback, ThumbnailContentFormat format, int pixelWidth, int pixelHeight)
	{
		if (content.IsEmpty)
		{
			throw new ArgumentException("Thumbnail content cannot be empty.", nameof(content));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		if (format is ThumbnailContentFormat.Bgra8)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);

			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

			if (content.Length != checked(pixelWidth * pixelHeight * 4))
			{
				throw new ArgumentException("BGRA8 thumbnail content must contain exactly four bytes per pixel.", nameof(content));
			}
		}
		else if (format is ThumbnailContentFormat.EncodedImage)
		{
			if (pixelWidth is not 0 || pixelHeight is not 0)
			{
				throw new ArgumentException("Encoded thumbnail content cannot specify raw pixel dimensions.", nameof(pixelWidth));
			}
		}
		else
		{
			throw new ArgumentOutOfRangeException(nameof(format));
		}

		Content = content;
		ContentType = contentType;
		IsFallback = isFallback;
		Format = format;
		PixelWidth = pixelWidth;
		PixelHeight = pixelHeight;
	}
}

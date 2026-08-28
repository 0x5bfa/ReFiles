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

	/// <summary>Initializes a thumbnail cache entry.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of the content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
	public ThumbnailCacheEntry(byte[] content, string contentType, bool isFallback = false)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (content.Length is 0)
		{
			throw new ArgumentException("Thumbnail content cannot be empty.", nameof(content));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		_content = (byte[])content.Clone();
		ContentType = contentType;
		IsFallback = isFallback;
	}

	internal ThumbnailResult CreateResult()
	{
		return new ThumbnailResult(Content, ContentType, IsFallback);
	}
}

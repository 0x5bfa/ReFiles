// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Contains an immutable thumbnail payload suitable for a shared cache.
/// </summary>
public sealed class ThumbnailCacheEntry
{
	private readonly byte[] content;

	public ThumbnailCacheEntry(byte[] content, string contentType, bool isFallback = false)
	{
		ArgumentNullException.ThrowIfNull(content);
		if (content.Length is 0)
			throw new ArgumentException("Thumbnail content cannot be empty.", nameof(content));

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		this.content = (byte[])content.Clone();
		ContentType = contentType;
		IsFallback = isFallback;
	}

	public ReadOnlyMemory<byte> Content => content;

	public string ContentType { get; }

	public bool IsFallback { get; }

	internal ThumbnailResult CreateResult()
	{
		return new ThumbnailResult(Content, ContentType, IsFallback);
	}
}

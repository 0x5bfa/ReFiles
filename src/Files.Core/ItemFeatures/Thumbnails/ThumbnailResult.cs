// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Thumbnails;

public sealed record ThumbnailResult
{
	public ReadOnlyMemory<byte> Content { get; }

	public string ContentType { get; }

	public bool IsFallback { get; }

	public ThumbnailResult(ReadOnlyMemory<byte> content, string contentType, bool isFallback)
	{
		if (content.IsEmpty)
		{
			throw new ArgumentException("Thumbnail content cannot be empty.", nameof(content));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		Content = content;
		ContentType = contentType;
		IsFallback = isFallback;
	}
}

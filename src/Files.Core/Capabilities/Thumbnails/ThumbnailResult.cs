// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Thumbnails;

/// <summary>Contains encoded thumbnail content and its presentation metadata.</summary>
public sealed record ThumbnailResult
{
	/// <summary>Gets the thumbnail bytes.</summary>
	public ReadOnlyMemory<byte> Content { get; }

	/// <summary>Gets the MIME type of the thumbnail content.</summary>
	public string ContentType { get; }

	/// <summary>Gets a value indicating whether the thumbnail is a fallback representation.</summary>
	public bool IsFallback { get; }

	/// <summary>Initializes a thumbnail result.</summary>
	/// <param name="content">The thumbnail bytes.</param>
	/// <param name="contentType">The MIME type of the content.</param>
	/// <param name="isFallback">Whether the content is a fallback representation.</param>
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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public sealed record PreviewContentType
{
	public string MediaType { get; }

	public PreviewContentType(string mediaType)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

		var separator = mediaType.IndexOf('/');
		if (separator <= 0 || separator == mediaType.Length - 1 || mediaType.Any(char.IsWhiteSpace))
		{
			throw new ArgumentException("The media type must contain non-empty type and subtype parts.", nameof(mediaType));
		}

		MediaType = mediaType;
	}
}

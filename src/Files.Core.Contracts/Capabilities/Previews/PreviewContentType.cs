// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Represents the media type of preview content.</summary>
public sealed record PreviewContentType
{
	/// <summary>Gets the media type value.</summary>
	public string MediaType { get; }

	/// <summary>Initializes a media type.</summary>
	/// <param name="mediaType">The media type in type/subtype form.</param>
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

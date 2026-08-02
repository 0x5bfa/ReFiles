// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Thumbnails;
using System.Collections.ObjectModel;

namespace Files.Core.Browsing;

/// <summary>
/// Contains prefetched, UI-agnostic presentation data for one browse item snapshot.
/// </summary>
public sealed record BrowseItemPresentation
{
	public BrowseItemPresentation(IReadOnlyDictionary<string, object?>? properties = null, ThumbnailResult? thumbnail = null)
	{
		Properties = new ReadOnlyDictionary<string, object?>(
			new Dictionary<string, object?>(properties ?? new Dictionary<string, object?>(), StringComparer.Ordinal));
		Thumbnail = thumbnail is null
			? null
			: new ThumbnailResult(thumbnail.Content.ToArray(), thumbnail.ContentType, thumbnail.IsFallback);
	}

	public IReadOnlyDictionary<string, object?> Properties { get; }

	public ThumbnailResult? Thumbnail { get; }
}

public sealed class BrowseItemPresentationChangedEventArgs : EventArgs
{
	public BrowseItemPresentationChangedEventArgs(StorableKey key, BrowseItemPresentation presentation)
	{
		ArgumentNullException.ThrowIfNull(presentation);

		Key = key;
		Presentation = presentation;
	}

	public StorableKey Key { get; }

	public BrowseItemPresentation Presentation { get; }
}

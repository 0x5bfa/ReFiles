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
	public IReadOnlyDictionary<string, object?> Properties { get; }

	public ThumbnailResult? Thumbnail { get; }

	public BrowseItemPresentation(IReadOnlyDictionary<string, object?>? properties = null, ThumbnailResult? thumbnail = null)
	{
		Properties = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(properties ?? new Dictionary<string, object?>(), StringComparer.Ordinal));
		Thumbnail = thumbnail is null
			? null
			: new ThumbnailResult(thumbnail.Content.ToArray(), thumbnail.ContentType, thumbnail.IsFallback);
	}
}

/// <summary>Identifies the presentation fields changed for a browse item.</summary>
[Flags]
public enum BrowseItemPresentationChangeFlags
{
	/// <summary>No presentation field changed.</summary>
	None = 0,

	/// <summary>The property values changed.</summary>
	Properties = 1 << 0,

	/// <summary>The thumbnail changed.</summary>
	Thumbnail = 1 << 1,
}

/// <summary>Describes a presentation update for one browse item.</summary>
public sealed class BrowseItemPresentationChangedEventArgs : EventArgs
{
	/// <summary>Gets the updated item key.</summary>
	public StorableKey Key { get; }

	/// <summary>Gets the current presentation snapshot.</summary>
	public BrowseItemPresentation Presentation { get; }

	/// <summary>Gets the fields changed by this update.</summary>
	public BrowseItemPresentationChangeFlags Changed { get; }

	/// <summary>Initializes a presentation update event.</summary>
	/// <param name="key">The updated item key.</param>
	/// <param name="presentation">The current presentation snapshot.</param>
	/// <param name="changed">The fields changed by the update.</param>
	public BrowseItemPresentationChangedEventArgs(
		StorableKey key,
		BrowseItemPresentation presentation,
		BrowseItemPresentationChangeFlags changed = BrowseItemPresentationChangeFlags.Properties | BrowseItemPresentationChangeFlags.Thumbnail)
	{
		ArgumentNullException.ThrowIfNull(presentation);

		Key = key;
		Presentation = presentation;
		Changed = changed;
	}
}

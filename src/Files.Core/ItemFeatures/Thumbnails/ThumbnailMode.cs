// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>Specifies how a thumbnail should be selected.</summary>
public enum ThumbnailMode
{
	/// <summary>Use the item's icon.</summary>
	Icon,
	/// <summary>Use content thumbnail data.</summary>
	Content,
	/// <summary>Prefer content data and fall back to the icon.</summary>
	PreferContent,
}

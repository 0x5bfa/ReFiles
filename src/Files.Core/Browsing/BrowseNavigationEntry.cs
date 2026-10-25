// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Thumbnails;

namespace Files.Core.Browsing;

/// <summary>Contains navigation data without presentation resources or provider objects.</summary>
/// <param name="Text">The provider-supplied name.</param>
/// <param name="Location">The navigation target.</param>
/// <param name="HasChildren">Whether child locations are available.</param>
/// <param name="Thumbnail">The optional encoded icon.</param>
public sealed record BrowseNavigationEntry(string Text, BrowseLocation Location, bool HasChildren, ThumbnailResult? Thumbnail = null);

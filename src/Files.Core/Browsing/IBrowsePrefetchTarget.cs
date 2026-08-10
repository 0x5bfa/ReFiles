// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>
/// Internal session boundary used to reject and publish prefetched snapshot data.
/// </summary>
internal interface IBrowsePrefetchTarget
{
	ValueTask<bool> PublishPropertiesAsync(long generation, IStorableModel item, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken);

	ValueTask<bool> PublishThumbnailAsync(long generation, IStorableModel item, ThumbnailResult thumbnail, CancellationToken cancellationToken);
}

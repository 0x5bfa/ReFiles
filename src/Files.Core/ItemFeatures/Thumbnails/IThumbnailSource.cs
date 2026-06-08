// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>
/// Supplies thumbnails without also claiming to be a storage item.
/// </summary>
public interface IThumbnailSource
{
	ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default);
}

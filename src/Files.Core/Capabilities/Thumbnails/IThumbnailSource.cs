// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Thumbnails;

/// <summary>
/// Supplies thumbnails without also claiming to be a storage item.
/// </summary>
public interface IThumbnailSource
{
	/// <summary>Gets a thumbnail for an item.</summary>
	/// <param name="request">The thumbnail request.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The thumbnail result, or <see langword="null"/> when no thumbnail is available.</returns>
	ValueTask<ThumbnailResult?> GetThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default);
}

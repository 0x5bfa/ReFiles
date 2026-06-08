// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Produces UI-neutral preview content for one item.
/// </summary>
public interface IPreviewSource
{
	ValueTask<PreviewResult?> GetPreviewAsync(
		PreviewRequest request,
		CancellationToken cancellationToken = default);
}

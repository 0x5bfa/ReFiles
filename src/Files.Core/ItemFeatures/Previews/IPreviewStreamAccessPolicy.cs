// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

public interface IPreviewStreamAccessPolicy
{
	ValueTask<PreviewBlockReason?> GetBlockReasonAsync(
		PreviewRequest request,
		ItemContext context,
		CancellationToken cancellationToken = default);
}

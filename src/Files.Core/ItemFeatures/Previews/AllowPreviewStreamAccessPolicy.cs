// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Allows stream previews without applying hydration or trust restrictions.
/// </summary>
public sealed class AllowPreviewStreamAccessPolicy
	: IPreviewStreamAccessPolicy
{
	public static AllowPreviewStreamAccessPolicy Instance { get; } = new();

	private AllowPreviewStreamAccessPolicy()
	{
	}

	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult<PreviewBlockReason?>(null);
	}
}

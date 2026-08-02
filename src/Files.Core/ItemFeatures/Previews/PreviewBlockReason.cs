// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public enum PreviewBlockReason
{
	RequiresHydration,
	TooLarge,
	AccessDenied,
	DisabledByPolicy,
}

/// <summary>
/// Indicates that a loader understands the item but cannot preview it under the current policy.
/// </summary>
public sealed class BlockedPreviewResult : PreviewResult
{
	public PreviewBlockReason Reason { get; }

	public BlockedPreviewResult(PreviewBlockReason reason)
	{
		if (reason is not PreviewBlockReason.RequiresHydration and not PreviewBlockReason.TooLarge and not PreviewBlockReason.AccessDenied and not PreviewBlockReason.DisabledByPolicy)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Explains why preview generation is blocked.</summary>
public enum PreviewBlockReason
{
	/// <summary>Preview generation would require content hydration.</summary>
	RequiresHydration,
	/// <summary>The content exceeds the configured preview size limit.</summary>
	TooLarge,
	/// <summary>The content cannot be accessed.</summary>
	AccessDenied,
	/// <summary>A preview policy disabled the provider.</summary>
	DisabledByPolicy,
}

/// <summary>
/// Indicates that a loader understands the item but cannot preview it under the current policy.
/// </summary>
public sealed class BlockedPreviewResult : PreviewResult
{
	/// <summary>Gets the reason the preview was blocked.</summary>
	public PreviewBlockReason Reason { get; }

	/// <summary>Initializes a blocked preview result.</summary>
	/// <param name="reason">The blocking reason.</param>
	public BlockedPreviewResult(PreviewBlockReason reason)
	{
		if (reason is not PreviewBlockReason.RequiresHydration and not PreviewBlockReason.TooLarge and not PreviewBlockReason.AccessDenied and not PreviewBlockReason.DisabledByPolicy)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
	}
}

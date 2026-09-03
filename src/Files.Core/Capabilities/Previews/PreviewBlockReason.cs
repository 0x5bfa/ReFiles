// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Explains why preview generation is blocked.</summary>
public enum PreviewBlockReason
{
	/// <summary>Preview generation would require content hydration.</summary>
	RequiresHydration = 0,
	/// <summary>The content exceeds the configured preview size limit.</summary>
	TooLarge = 1,
	/// <summary>The content cannot be accessed.</summary>
	AccessDenied = 2,
	/// <summary>A preview policy disabled the provider.</summary>
	DisabledByPolicy = 3,
	/// <summary>The content did not pass the Windows trust checks.</summary>
	Untrusted = 4,
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
		if (reason is not PreviewBlockReason.RequiresHydration and not PreviewBlockReason.TooLarge and not PreviewBlockReason.AccessDenied
			and not PreviewBlockReason.Untrusted and not PreviewBlockReason.DisabledByPolicy)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
	}
}

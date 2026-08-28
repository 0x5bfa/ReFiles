// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>Specifies the limits and hydration policy for a preview request.</summary>
public enum PreviewHydrationPolicy
{
	/// <summary>Use content that is already available locally.</summary>
	LocalOnly,
	/// <summary>Allow the preview provider to hydrate content.</summary>
	AllowHydration,
}

/// <summary>Describes the limits applied to a preview request.</summary>
public sealed record PreviewRequest
{
	/// <summary>Gets the maximum number of bytes that may be read.</summary>
	public long? MaximumBytes { get; }

	/// <summary>Gets the policy controlling content hydration.</summary>
	public PreviewHydrationPolicy HydrationPolicy { get; }

	/// <summary>Initializes a preview request.</summary>
	/// <param name="maximumBytes">The maximum number of bytes to read.</param>
	/// <param name="hydrationPolicy">The content hydration policy.</param>
	public PreviewRequest(long? maximumBytes = null, PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly)
	{
		if (maximumBytes is not null)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes.Value);

		}

		if (hydrationPolicy is not PreviewHydrationPolicy.LocalOnly and not PreviewHydrationPolicy.AllowHydration)
		{
			throw new ArgumentOutOfRangeException(nameof(hydrationPolicy));
		}

		MaximumBytes = maximumBytes;
		HydrationPolicy = hydrationPolicy;
	}
}

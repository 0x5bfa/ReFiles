// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public enum PreviewHydrationPolicy
{
	LocalOnly,
	AllowHydration,
}

public sealed record PreviewRequest
{
	public PreviewRequest(long? maximumBytes = null, PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly)
	{
		if (maximumBytes is not null)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes.Value);
		}

		if (hydrationPolicy is not PreviewHydrationPolicy.LocalOnly
			and not PreviewHydrationPolicy.AllowHydration)
		{
			throw new ArgumentOutOfRangeException(nameof(hydrationPolicy));
		}

		MaximumBytes = maximumBytes;
		HydrationPolicy = hydrationPolicy;
	}

	public long? MaximumBytes { get; }

	public PreviewHydrationPolicy HydrationPolicy { get; }
}

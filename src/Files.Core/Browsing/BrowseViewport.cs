// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>
/// Describes the visible item range and the number of surrounding items to prefetch.
/// </summary>
public sealed record BrowseViewport
{
	public BrowseViewport(int firstVisibleIndex, int visibleCount, int lookAheadCount = 20, int dpi = 96)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(firstVisibleIndex);
		ArgumentOutOfRangeException.ThrowIfNegative(visibleCount);
		ArgumentOutOfRangeException.ThrowIfNegative(lookAheadCount);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

		FirstVisibleIndex = firstVisibleIndex;
		VisibleCount = visibleCount;
		LookAheadCount = lookAheadCount;
		Dpi = dpi;
	}

	public int FirstVisibleIndex { get; }

	public int VisibleCount { get; }

	/// <summary>
	/// Gets the maximum number of items prefetched on each side of the visible range.
	/// </summary>
	public int LookAheadCount { get; }

	/// <summary>
	/// Gets the display density used for DPI-aware thumbnail requests.
	/// </summary>
	public int Dpi { get; }
}

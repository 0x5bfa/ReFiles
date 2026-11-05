// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>
/// Immutable representation of a pane's navigation history.
/// </summary>
public sealed record BrowseNavigationHistoryState
{
	/// <summary>Gets the committed locations.</summary>
	public IReadOnlyList<BrowseLocation> Entries { get; }

	/// <summary>Gets the index of the current location.</summary>
	public int CurrentIndex { get; }

	/// <summary>Gets the current location.</summary>
	public BrowseLocation? Current => CurrentIndex < 0 ? null : Entries[CurrentIndex];

	/// <summary>Initializes an immutable navigation history state.</summary>
	/// <param name="entries">The committed locations.</param>
	/// <param name="currentIndex">The current entry index, or <c>-1</c> for an empty history.</param>
	public BrowseNavigationHistoryState(IEnumerable<BrowseLocation> entries, int currentIndex)
	{
		ArgumentNullException.ThrowIfNull(entries);

		var entryArray = entries.ToArray();
		if (entryArray.Any(static entry => entry is null))
		{
			throw new ArgumentException("History entries cannot contain null values.", nameof(entries));
		}

		if (entryArray.Length is 0)
		{
			if (currentIndex is not -1)
			{
				throw new ArgumentOutOfRangeException(nameof(currentIndex));
			}
		}
		else if (currentIndex < 0 || currentIndex >= entryArray.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(currentIndex));
		}

		Entries = Array.AsReadOnly(entryArray);
		CurrentIndex = currentIndex;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Represents the selected, focused, and anchor items in a browse session.</summary>
/// <param name="SelectedKeys">The selected item keys.</param>
/// <param name="FocusedKey">The key of the focused item, if any.</param>
/// <param name="AnchorKey">The key used as the range-selection anchor, if any.</param>
public sealed record BrowseSelectionState(IReadOnlyList<StorableKey> SelectedKeys, StorableKey? FocusedKey, StorableKey? AnchorKey)
{
	/// <summary>Gets an empty selection state.</summary>
	public static BrowseSelectionState Empty { get; } = new(Array.AsReadOnly(Array.Empty<StorableKey>()), null, null);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

public sealed record BrowseSelectionState(IReadOnlyList<StorableKey> SelectedKeys, StorableKey? FocusedKey, StorableKey? AnchorKey)
{
	public static BrowseSelectionState Empty { get; } = new(Array.AsReadOnly(Array.Empty<StorableKey>()), null, null);
}

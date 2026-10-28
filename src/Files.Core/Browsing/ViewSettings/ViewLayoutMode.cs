// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>
/// Specifies how browse items are arranged.
/// </summary>
public enum ViewLayoutMode
{
	/// <summary>Displays items in a detailed table.</summary>
	Details,

	/// <summary>Displays items in a compact list.</summary>
	List,

	/// <summary>Displays items as information cards.</summary>
	Cards,

	/// <summary>Displays items in a thumbnail grid.</summary>
	Grid,

	/// <summary>Displays items in hierarchical columns.</summary>
	Columns,
}

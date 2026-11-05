// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Specifies the split layout of a tab.</summary>
public enum PaneSplitOrientation
{
	/// <summary>The tab contains one pane.</summary>
	None,

	/// <summary>The panes are arranged side by side.</summary>
	Vertical,

	/// <summary>The panes are arranged one above the other.</summary>
	Horizontal,
}

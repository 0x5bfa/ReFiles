// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Models;

namespace Files.Core.Sessions;

/// <summary>Specifies how navigation updates pane history.</summary>
public enum PaneNavigationMode
{
	/// <summary>Adds the target after the current history entry.</summary>
	Push,

	/// <summary>Replaces the current history entry.</summary>
	Replace,

	/// <summary>Adds the search target unless the current committed entry is another search, which it replaces.</summary>
	UpdateSearch,

	/// <summary>Moves back to a matching search origin when possible; otherwise replaces the current entry.</summary>
	ExitSearch,
}

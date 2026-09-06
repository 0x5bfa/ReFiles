// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Identifies the browse view settings supplied by an override.</summary>
[Flags]
public enum ViewSettingsOverrideFields
{
	/// <summary>Specifies that no settings are overridden.</summary>
	None = 0,

	/// <summary>Specifies that the layout mode is overridden.</summary>
	LayoutMode = 1 << 0,

	/// <summary>Specifies that the Details view column layout is overridden.</summary>
	DetailsColumns = 1 << 1,

	/// <summary>Specifies that the sort property is overridden.</summary>
	SortPropertyId = 1 << 2,

	/// <summary>Specifies that the sort direction is overridden.</summary>
	SortDirection = 1 << 3,

	/// <summary>Specifies that the preferred item size is overridden.</summary>
	ItemSize = 1 << 4,

	/// <summary>Specifies that the group property is overridden.</summary>
	GroupPropertyId = 1 << 5,

	/// <summary>Specifies that the group direction is overridden.</summary>
	GroupDirection = 1 << 6,

	/// <summary>Specifies that every browse view setting is overridden.</summary>
	All = LayoutMode | DetailsColumns | SortPropertyId | SortDirection | ItemSize | GroupPropertyId | GroupDirection,
}

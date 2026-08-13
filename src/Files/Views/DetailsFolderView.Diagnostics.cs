// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;

namespace Files.Views;

public sealed partial class DetailsFolderView
{
	/// <summary>Gets the production table used by the details view for internal diagnostics and UI tests.</summary>
	internal TableView PerformanceTable => ItemTable;
}

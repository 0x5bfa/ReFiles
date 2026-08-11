// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Files.UITests.Views;

public sealed partial class TableViewPage : Page
{
	public ObservableCollection<TableViewSampleItem> Items { get; } =
	[
		new("Documents", "File folder", DateTimeOffset.Now.AddDays(-2), true),
		new("Notes.txt", "Text Document", DateTimeOffset.Now.AddHours(-3), false),
		new("Archive.zip", "Compressed Folder", DateTimeOffset.Now.AddMinutes(-45), true),
	];

	public TableViewPage()
	{
		InitializeComponent();
	}
}

public sealed class TableViewSampleItem : ITableViewCellValueProvider
{
	public string Name { get; }

	public string Type { get; }

	public DateTimeOffset Modified { get; }

	public bool IsPinned { get; }

	public TableViewSampleItem(string name, string type, DateTimeOffset modified, bool isPinned)
	{
		Name = name;
		Type = type;
		Modified = modified;
		IsPinned = isPinned;
	}

	public string GetDisplayText(string columnId)
	{
		return columnId switch
		{
			nameof(Name) => Name,
			nameof(Type) => Type,
			nameof(Modified) => Modified.ToString("g", CultureInfo.CurrentCulture),
			_ => string.Empty,
		};
	}
}

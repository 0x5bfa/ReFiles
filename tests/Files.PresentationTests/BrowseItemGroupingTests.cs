// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.PresentationTests;

[TestClass]
public sealed class BrowseItemGroupingTests
{
	private static readonly BrowseGroupingText Text = new("Folders", "files", "Unspecified", "Tiny", "Small", "Medium", "Large", "Very large", "Huge");

	[TestMethod]
	public void NameGroupsFollowTheRequestedDirection()
	{
		var items = new[]
		{
			CreateItem("alpha", "Alpha", isFolder: false),
			CreateItem("beta", "Beta", isFolder: false),
			CreateItem("apple", "Apple", isFolder: false),
		};

		var ascending = BrowseItemGrouping.Create(items, BrowseDisplayPropertyIds.Name, ViewSortDirection.Ascending, Text);
		var descending = BrowseItemGrouping.Create(items, BrowseDisplayPropertyIds.Name, ViewSortDirection.Descending, Text);

		CollectionAssert.AreEqual(new[] {"A", "B"}, ascending.Select(static group => group.Title).ToArray());
		CollectionAssert.AreEqual(new[] {"B", "A"}, descending.Select(static group => group.Title).ToArray());
		Assert.AreEqual(2, ascending[0].Count);
	}

	[TestMethod]
	public void TypeGroupingKeepsFoldersFirstWhenDescending()
	{
		var folder = CreateItem("folder", "Folder", isFolder: true);
		var image = CreateItem("image", "Image.png", isFolder: false, (BrowseDisplayPropertyIds.Type, "Image"));
		var text = CreateItem("text", "Text.txt", isFolder: false, (BrowseDisplayPropertyIds.Type, "Text"));

		var groups = BrowseItemGrouping.Create([image, folder, text], BrowseDisplayPropertyIds.Type, ViewSortDirection.Descending, Text);

		CollectionAssert.AreEqual(new[] {"Folders", "Text", "Image"}, groups.Select(static group => group.Title).ToArray());
	}

	[TestMethod]
	public void SizeGroupingUsesExplorerStyleBuckets()
	{
		var tiny = CreateItem("tiny", "Tiny.bin", isFolder: false, (BrowseDisplayPropertyIds.Size, 100L));
		var small = CreateItem("small", "Small.bin", isFolder: false, (BrowseDisplayPropertyIds.Size, 32L * 1024));
		var medium = CreateItem("medium", "Medium.bin", isFolder: false, (BrowseDisplayPropertyIds.Size, 2L * 1024 * 1024));
		var huge = CreateItem("huge", "Huge.bin", isFolder: false, (BrowseDisplayPropertyIds.Size, 5L * 1024 * 1024 * 1024));

		var groups = BrowseItemGrouping.Create([huge, medium, small, tiny], BrowseDisplayPropertyIds.Size, ViewSortDirection.Ascending, Text);

		CollectionAssert.AreEqual(new[] {"Tiny", "Small", "Medium", "Huge"}, groups.Select(static group => group.Title).ToArray());
	}

	private static BrowseItemViewModel CreateItem(string id, string name, bool isFolder, params (string PropertyId, object? Value)[] properties)
	{
		var reference = new StorableReference(new StorageSourceId("test"), id, new StorageAddress("test", id));
		var item = new BrowseItemViewModel(name, isFolder, reference);
		if (properties.Length is not 0)
		{
			item.SetProperties(properties.ToDictionary(static property => property.PropertyId, static property => property.Value, StringComparer.Ordinal));
		}

		return item;
	}
}

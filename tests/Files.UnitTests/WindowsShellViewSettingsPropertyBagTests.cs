// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Contains tests for the ReFiles-owned Shell property-bag payload.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowsShellViewSettingsPropertyBagTests
{
	/// <summary>Test case: the managed string variant uses the property-bag BSTR representation.</summary>
	[TestMethod]
	public void ManagedStringVariantUsesBstr()
	{
		using var value = ComVariant.Create("value");

		Assert.AreEqual(VarEnum.VT_BSTR, value.VarType);
	}

	/// <summary>Test case: every persisted field round-trips through the versioned payload.</summary>
	[TestMethod]
	public void CodecRoundTripsCompleteOverride()
	{
		var columns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 280, 0),
			new ViewColumnSettings("System.Size", 96, 1, isVisible: false),
		};
		var values = new BrowseViewSettings(ViewLayoutMode.Columns, columns, "System.DateModified", ViewSortDirection.Descending, 72, "System.ItemTypeText", ViewSortDirection.Ascending);
		var expected = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.All, values, ViewColumnSettingsMode.Insert);

		var json = WindowsShellViewSettingsPropertyBag.Serialize(expected);
		var actual = WindowsShellViewSettingsPropertyBag.Deserialize(json);

		using var document = JsonDocument.Parse(json);
		Assert.AreEqual(WindowsShellViewSettingsPropertyBag.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
		Assert.IsNotNull(actual);
		Assert.AreEqual(expected.Fields, actual.Fields);
		Assert.AreEqual(expected.ColumnMode, actual.ColumnMode);
		Assert.AreEqual(expected.Values.LayoutMode, actual.Values.LayoutMode);
		CollectionAssert.AreEqual(expected.Values.Columns.ToArray(), actual.Values.Columns.ToArray());
		Assert.AreEqual(expected.Values.SortPropertyId, actual.Values.SortPropertyId);
		Assert.AreEqual(expected.Values.SortDirection, actual.Values.SortDirection);
		Assert.AreEqual(expected.Values.ItemSize, actual.Values.ItemSize);
		Assert.AreEqual(expected.Values.GroupPropertyId, actual.Values.GroupPropertyId);
		Assert.AreEqual(expected.Values.GroupDirection, actual.Values.GroupDirection);
	}

	/// <summary>Test case: malformed, unsupported, and invalid payloads do not become active settings.</summary>
	/// <param name="json">The payload to decode.</param>
	[TestMethod]
	[DataRow("")]
	[DataRow("{")]
	[DataRow("{\"schemaVersion\":2,\"settings\":{}}")]
	[DataRow("{\"schemaVersion\":1,\"settings\":{\"fields\":128,\"columnMode\":0,\"values\":{\"layoutMode\":0,\"detailsColumns\":[],\"sortDirection\":0,\"groupDirection\":0}}}")]
	public void CodecRejectsInvalidPayload(string json)
	{
		Assert.IsNull(WindowsShellViewSettingsPropertyBag.Deserialize(json));
	}

	/// <summary>Test case: a Windows folder stores and retrieves supported view settings through its Shell-managed property bag.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsFolderRoundTripsShellManagedViewSettings()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ShellViewSettingsTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);

		try
		{
			await using var scheduler = new WindowsShellScheduler();
			await using var source = new WindowsStorageSource(scheduler: scheduler);
			var folder = (WindowsFolder)await source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, directoryPath));
			var columnSet = await folder.GetColumnsAsync();
			var columns = columnSet.DefaultVisible.Take(2).Select(static (column, index) => new ViewColumnSettings(column.PropertyId, 160 + index * 40, index)).ToArray();
			Assert.IsTrue(columns.Length > 0);
			var fields = ViewSettingsOverrideFields.LayoutMode | ViewSettingsOverrideFields.DetailsColumns | ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection;
			var values = new BrowseViewSettings(ViewLayoutMode.List, columns, columns[0].PropertyId, ViewSortDirection.Descending);
			var requested = new BrowseViewSettingsOverride(fields, values);

			var result = await folder.SetViewSettingsAsync(requested);
			var loaded = await folder.GetViewSettingsAsync();

			Assert.AreEqual(ViewSettingsOverrideFields.None, result.ApplicationSettings.Fields);
			Assert.IsNotNull(loaded);
			Assert.AreEqual(fields, loaded.Fields);
			Assert.AreEqual(ViewLayoutMode.List, loaded.Values.LayoutMode);
			CollectionAssert.AreEqual(columns, loaded.Values.Columns.ToArray());
			Assert.AreEqual(columns[0].PropertyId, loaded.Values.SortPropertyId);
			Assert.AreEqual(ViewSortDirection.Descending, loaded.Values.SortDirection);

			var remaining = await folder.ClearViewSettingsAsync(ViewSettingsOverrideFields.All);
			Assert.AreEqual(ViewSettingsOverrideFields.None, remaining.Fields);
			Assert.IsNull(await folder.GetViewSettingsAsync());
		}
		finally
		{
			Directory.Delete(directoryPath, recursive: true);
		}
	}
}

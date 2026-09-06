// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using Files.Core.Browsing;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.UnitTests;

/// <summary>Contains tests for layered view settings persistence.</summary>
[TestClass]
public sealed class ViewSettingsStoreTests
{
	/// <summary>Test case: a field mask preserves inheritance and supports explicit null values.</summary>
	[TestMethod]
	public void OverrideAppliesOnlyMaskedFieldsIncludingNulls()
	{
		var inherited = new BrowseViewSettings(
			ViewLayoutMode.Grid,
			[new ViewColumnSettings("System.ItemNameDisplay", 240, 0)],
			"System.DateModified",
			ViewSortDirection.Descending,
			96,
			"System.ItemTypeText",
			ViewSortDirection.Descending);
		var values = new BrowseViewSettings(ViewLayoutMode.List, sortPropertyId: null, itemSize: null, groupPropertyId: null);
		var fields = ViewSettingsOverrideFields.LayoutMode | ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.ItemSize | ViewSettingsOverrideFields.GroupPropertyId;
		var settingsOverride = new BrowseViewSettingsOverride(fields, values);

		var effective = settingsOverride.ApplyTo(inherited);

		Assert.AreEqual(ViewLayoutMode.List, effective.LayoutMode);
		CollectionAssert.AreEqual(inherited.Columns.ToArray(), effective.Columns.ToArray());
		Assert.IsNull(effective.SortPropertyId);
		Assert.AreEqual(ViewSortDirection.Descending, effective.SortDirection);
		Assert.IsNull(effective.ItemSize);
		Assert.IsNull(effective.GroupPropertyId);
		Assert.AreEqual(ViewSortDirection.Descending, effective.GroupDirection);
	}

	/// <summary>Test case: merging uses the supplied fields from the higher-priority layer.</summary>
	[TestMethod]
	public void MergeUsesHigherPriorityFields()
	{
		var lowerValues = new BrowseViewSettings(ViewLayoutMode.List, sortPropertyId: "System.ItemNameDisplay");
		var lower = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode | ViewSettingsOverrideFields.SortPropertyId, lowerValues);
		var higherValues = new BrowseViewSettings(sortPropertyId: null, sortDirection: ViewSortDirection.Descending);
		var higher = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection, higherValues);

		var merged = lower.Merge(higher);
		var effective = merged.ApplyTo(BrowseViewSettings.Default);

		Assert.AreEqual(ViewSettingsOverrideFields.LayoutMode | ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection, merged.Fields);
		Assert.AreEqual(ViewLayoutMode.List, effective.LayoutMode);
		Assert.IsNull(effective.SortPropertyId);
		Assert.AreEqual(ViewSortDirection.Descending, effective.SortDirection);
	}

	/// <summary>Test case: inserted columns use absolute positions while retaining and updating inherited columns.</summary>
	[TestMethod]
	public void InsertColumnsRetainsInheritedColumnsAndAppliesPersistedPresentation()
	{
		var inheritedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
			new ViewColumnSettings("System.DateModified", 160, 2),
		};
		var insertedColumns = new[]
		{
			new ViewColumnSettings("ReFiles.Tag", 120, 1),
			new ViewColumnSettings("System.Size", 144, 3, isVisible: false),
		};
		var inherited = new BrowseViewSettings(ViewLayoutMode.Details, inheritedColumns, "System.DateModified", ViewSortDirection.Descending);
		var settingsOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: insertedColumns), ViewColumnSettingsMode.Insert);

		var effective = settingsOverride.ApplyTo(inherited);

		var expectedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("ReFiles.Tag", 120, 1),
			new ViewColumnSettings("System.DateModified", 160, 2),
			new ViewColumnSettings("System.Size", 144, 3, isVisible: false),
		};
		CollectionAssert.AreEqual(expectedColumns, effective.Columns.ToArray());
		Assert.AreEqual(ViewLayoutMode.Details, effective.LayoutMode);
		Assert.AreEqual("System.DateModified", effective.SortPropertyId);
		Assert.AreEqual(ViewSortDirection.Descending, effective.SortDirection);
	}

	/// <summary>Test case: a replace layer and a higher-priority insert layer merge into one complete column layout.</summary>
	[TestMethod]
	public void MergeCombinesReplaceColumnsWithHigherPriorityInsertColumns()
	{
		var providerColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
		};
		var applicationColumns = new[] { new ViewColumnSettings("ReFiles.Tag", 120, 1) };
		var provider = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: providerColumns));
		var application = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: applicationColumns), ViewColumnSettingsMode.Insert);

		var merged = provider.Merge(application);
		var effective = merged.ApplyTo(BrowseViewSettings.Default);

		var expectedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("ReFiles.Tag", 120, 1),
			new ViewColumnSettings("System.Size", 100, 2),
		};
		Assert.AreEqual(ViewColumnSettingsMode.Replace, merged.ColumnMode);
		CollectionAssert.AreEqual(expectedColumns, effective.Columns.ToArray());
	}

	/// <summary>Test case: merging unrelated lower fields does not normalize absolute positions in an inserted column layer.</summary>
	[TestMethod]
	public void MergePreservesInsertPositionsWhenLowerLayerDoesNotSupplyColumns()
	{
		var lower = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.Columns));
		var applicationColumns = new[] { new ViewColumnSettings("ReFiles.Tag", 120, 2) };
		var higher = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: applicationColumns), ViewColumnSettingsMode.Insert);
		var providerColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
			new ViewColumnSettings("System.DateModified", 160, 2),
		};

		var merged = lower.Merge(higher);
		var effective = merged.ApplyTo(new BrowseViewSettings(columns: providerColumns));

		var expectedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
			new ViewColumnSettings("ReFiles.Tag", 120, 2),
			new ViewColumnSettings("System.DateModified", 160, 3),
		};
		Assert.AreEqual(ViewLayoutMode.Columns, effective.LayoutMode);
		Assert.AreEqual(ViewColumnSettingsMode.Insert, merged.ColumnMode);
		CollectionAssert.AreEqual(expectedColumns, effective.Columns.ToArray());
	}

	/// <summary>Test case: a higher-priority inserted-column field replaces an earlier insertion layer.</summary>
	[TestMethod]
	public void MergeReplacesLowerPriorityInsertColumns()
	{
		var inheritedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
		};
		var lowerColumns = new[] { new ViewColumnSettings("ReFiles.Tag", 120, 1) };
		var higherColumns = new[] { new ViewColumnSettings("ReFiles.GitStatus", 96, 2) };
		var lower = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: lowerColumns), ViewColumnSettingsMode.Insert);
		var higher = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.DetailsColumns, new BrowseViewSettings(columns: higherColumns), ViewColumnSettingsMode.Insert);

		var merged = lower.Merge(higher);
		var effective = merged.ApplyTo(new BrowseViewSettings(columns: inheritedColumns));

		var expectedColumns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 240, 0),
			new ViewColumnSettings("System.Size", 100, 1),
			new ViewColumnSettings("ReFiles.GitStatus", 96, 2),
		};
		Assert.AreEqual(ViewColumnSettingsMode.Insert, merged.ColumnMode);
		CollectionAssert.AreEqual(expectedColumns, effective.Columns.ToArray());
	}

	/// <summary>Test case: stable scope keys exclude recovery hints and search query text.</summary>
	[TestMethod]
	public void ScopeKeysUseStableLocationIdentityWithoutSearchQuery()
	{
		var sourceId = new StorageSourceId("test/source");
		var firstReference = new StorableReference(sourceId, "folder/item", new StorageAddress("path", "C:\\First"));
		var secondReference = new StorableReference(sourceId, "folder/item", new StorageAddress("path", "D:\\Moved"));
		var otherReference = new StorableReference(sourceId, "other", new StorageAddress("path", "C:\\Other"));

		Assert.AreEqual(ViewSettingsScopeKey.ForLocation(new FolderLocation(firstReference)), ViewSettingsScopeKey.ForLocation(new FolderLocation(secondReference)));
		Assert.AreEqual(ViewSettingsScopeKey.ForLocation(new SearchLocation("alpha", firstReference)), ViewSettingsScopeKey.ForLocation(new SearchLocation("private query", secondReference)));
		Assert.AreEqual(ViewSettingsScopeKey.ForLocation(new SearchLocation("alpha")), ViewSettingsScopeKey.ForLocation(new SearchLocation("beta")));
		Assert.AreNotEqual(ViewSettingsScopeKey.ForLocation(new SearchLocation("alpha", firstReference)), ViewSettingsScopeKey.ForLocation(new SearchLocation("alpha", otherReference)));
		Assert.AreNotEqual(ViewSettingsScopeKey.ForLocation(new FolderLocation(firstReference)), ViewSettingsScopeKey.ForLocation(new SearchLocation("alpha", firstReference)));
		Assert.IsFalse(ViewSettingsScopeKey.ForLocation(new SearchLocation("private query", firstReference)).Value.Contains("private query", StringComparison.Ordinal));
	}

	/// <summary>Test case: custom browse locations can opt into stable persistence scopes.</summary>
	[TestMethod]
	public void CustomLocationsCanProvideStableScopes()
	{
		var expected = new ViewSettingsScopeKey("v1/custom/location");

		Assert.IsTrue(ViewSettingsScopeKey.TryForLocation(new ScopedTestBrowseLocation(expected), out var actual));
		Assert.AreSame(expected, actual);
		Assert.IsFalse(ViewSettingsScopeKey.TryForLocation(new UnscopedTestBrowseLocation("temporary"), out actual));
		Assert.IsNull(actual);
	}

	/// <summary>Test case: the in-memory store persists and removes partial overrides by stable scope.</summary>
	[TestMethod]
	public async Task InMemoryStoreUsesStableScopesAndSupportsRemoveAsync()
	{
		var store = new InMemoryViewSettingsStore();
		var scope = new ViewSettingsScopeKey("v1/test/scope");
		var equivalentScope = new ViewSettingsScopeKey("v1/test/scope");
		var settingsOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.Cards));

		await store.SetAsync(scope, settingsOverride);

		Assert.AreSame(settingsOverride, await store.GetAsync(equivalentScope));
		Assert.IsTrue(await store.RemoveAsync(equivalentScope));
		Assert.IsNull(await store.GetAsync(scope));
		Assert.IsFalse(await store.RemoveAsync(scope));
	}

	/// <summary>Test case: JSON persistence round-trips masks, explicit null values, and custom columns.</summary>
	[TestMethod]
	public async Task JsonStoreRoundTripsOverridesAndRemovesThemDurably()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ViewSettingsTests-{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "view-settings.json");
		var reference = new StorableReference(new StorageSourceId("test"), "search-root");
		var scope = ViewSettingsScopeKey.ForLocation(new SearchLocation("private search query", reference));
		var columns = new[]
		{
			new ViewColumnSettings("System.ItemNameDisplay", 260, 0),
			new ViewColumnSettings("System.Size", 120, 1, isVisible: false),
		};
		var values = new BrowseViewSettings(ViewLayoutMode.Details, columns, sortPropertyId: null, itemSize: null);
		var fields = ViewSettingsOverrideFields.DetailsColumns | ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.ItemSize;
		var settingsOverride = new BrowseViewSettingsOverride(fields, values, ViewColumnSettingsMode.Insert);

		try
		{
			var writer = new JsonViewSettingsStore(filePath);
			await writer.SetAsync(scope, settingsOverride);
			await writer.SetAsync(scope, settingsOverride);

			var json = await File.ReadAllTextAsync(filePath);
			using var document = JsonDocument.Parse(json);
			Assert.AreEqual(JsonViewSettingsStore.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
			Assert.IsFalse(json.Contains("private search query", StringComparison.Ordinal));
			Assert.IsEmpty(Directory.GetFiles(directoryPath, "*.tmp"));

			var reader = new JsonViewSettingsStore(filePath);
			var loaded = await reader.GetAsync(scope);

			Assert.IsNotNull(loaded);
			Assert.AreEqual(settingsOverride.Fields, loaded.Fields);
			Assert.AreEqual(ViewColumnSettingsMode.Insert, loaded.ColumnMode);
			CollectionAssert.AreEqual(columns, loaded.Values.Columns.ToArray());
			Assert.IsNull(loaded.Values.SortPropertyId);
			Assert.IsNull(loaded.Values.ItemSize);

			Assert.IsTrue(await writer.RemoveAsync(scope));
			Assert.IsNull(await new JsonViewSettingsStore(filePath).GetAsync(scope));
			Assert.IsEmpty(Directory.GetFiles(directoryPath, "*.tmp"));
		}
		finally
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
	}

	/// <summary>Test case: independent stores merge writes made by multiple app processes.</summary>
	[TestMethod]
	public async Task JsonStoreMergesConcurrentWriters()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ViewSettingsConcurrencyTests-{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "view-settings.json");
		var firstScope = new ViewSettingsScopeKey("v1/test/first");
		var secondScope = new ViewSettingsScopeKey("v1/test/second");
		var firstOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.List));
		var secondOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.Columns));

		try
		{
			var firstStore = new JsonViewSettingsStore(filePath);
			var secondStore = new JsonViewSettingsStore(filePath);
			Assert.IsNull(await firstStore.GetAsync(firstScope));
			Assert.IsNull(await secondStore.GetAsync(secondScope));

			await Task.WhenAll(firstStore.SetAsync(firstScope, firstOverride).AsTask(), secondStore.SetAsync(secondScope, secondOverride).AsTask());

			var reader = new JsonViewSettingsStore(filePath);
			Assert.AreEqual(ViewLayoutMode.List, (await reader.GetAsync(firstScope))?.Values.LayoutMode);
			Assert.AreEqual(ViewLayoutMode.Columns, (await reader.GetAsync(secondScope))?.Values.LayoutMode);
		}
		finally
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
	}

	/// <summary>Test case: independent stores atomically transform unrelated fields in the same scope.</summary>
	[TestMethod]
	public async Task JsonStoreAtomicallyUpdatesOneScope()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ViewSettingsAtomicUpdateTests-{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "view-settings.json");
		var scope = new ViewSettingsScopeKey("v1/test/shared");
		var layoutOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.List));
		var sortValues = new BrowseViewSettings(sortPropertyId: "System.Size", sortDirection: ViewSortDirection.Descending);
		var sortOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection, sortValues);

		try
		{
			var firstStore = new JsonViewSettingsStore(filePath);
			var secondStore = new JsonViewSettingsStore(filePath);
			await Task.WhenAll(firstStore.PatchAsync(scope, layoutOverride.Fields, layoutOverride).AsTask(), secondStore.PatchAsync(scope, sortOverride.Fields, sortOverride).AsTask());

			var stored = await new JsonViewSettingsStore(filePath).GetAsync(scope);
			Assert.IsNotNull(stored);
			Assert.AreEqual(ViewSettingsOverrideFields.LayoutMode | ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection, stored.Fields);
			var effective = stored.ApplyTo(BrowseViewSettings.Default);
			Assert.AreEqual(ViewLayoutMode.List, effective.LayoutMode);
			Assert.AreEqual("System.Size", effective.SortPropertyId);
			Assert.AreEqual(ViewSortDirection.Descending, effective.SortDirection);
		}
		finally
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
	}

	/// <summary>Test case: unsupported durable schema versions are preserved and replaced by an empty active store.</summary>
	[TestMethod]
	public async Task JsonStoreQuarantinesUnsupportedSchemaVersion()
	{
		var directoryPath = Path.Combine(Path.GetTempPath(), $"Files.Core.ViewSettingsSchemaTests-{Guid.NewGuid():N}");
		var filePath = Path.Combine(directoryPath, "view-settings.json");
		Directory.CreateDirectory(directoryPath);

		try
		{
			const string json = "{\"schemaVersion\":2,\"entries\":{}}";
			await File.WriteAllTextAsync(filePath, json);
			var store = new JsonViewSettingsStore(filePath);
			var scope = new ViewSettingsScopeKey("v1/test/scope");

			Assert.IsNull(await store.GetAsync(scope));
			Assert.IsFalse(File.Exists(filePath));
			var invalidPath = Directory.GetFiles(directoryPath, "view-settings.invalid-*.json").Single();
			Assert.AreEqual(json, await File.ReadAllTextAsync(invalidPath));

			var settingsOverride = new BrowseViewSettingsOverride(ViewSettingsOverrideFields.LayoutMode, new BrowseViewSettings(ViewLayoutMode.Grid));
			await store.SetAsync(scope, settingsOverride);
			Assert.AreEqual(ViewLayoutMode.Grid, (await store.GetAsync(scope))?.Values.LayoutMode);
		}
		finally
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
	}

	private sealed record ScopedTestBrowseLocation(ViewSettingsScopeKey ViewSettingsScope) : BrowseLocation, IViewSettingsScopeProvider;

	private sealed record UnscopedTestBrowseLocation(string Id) : BrowseLocation;
}

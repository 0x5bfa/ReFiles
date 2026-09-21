// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for stable and capability-aware browse view settings.
/// </summary>
[TestClass]
public sealed class ViewSettingsTests
{
	/// <summary>
	/// Test case: location keys ignore mutable recovery addresses.
	/// </summary>
	[TestMethod]
	public void LocationKeysUseStableIdentity()
	{
		var source = new StorageSourceId("test");
		var firstReference = new StorableReference(source, "item", new StorageAddress("file", "C:\\first"));
		var secondReference = new StorableReference(source, "item", new StorageAddress("file", "C:\\second"));

		var firstKey = ViewSettingsKey.ForLocation(new FolderLocation(firstReference));
		var secondKey = ViewSettingsKey.ForLocation(new FolderLocation(secondReference));

		Assert.AreEqual(firstKey, secondKey);
	}

	/// <summary>
	/// Test case: search view state is shared by the search profile rather than query text.
	/// </summary>
	[TestMethod]
	public void SearchKeysIgnoreQueryText()
	{
		var scope = new StorableReference(new StorageSourceId("test"), "folder");

		var firstKey = ViewSettingsKey.ForLocation(new SearchLocation("one", scope));
		var secondKey = ViewSettingsKey.ForLocation(new SearchLocation("two", scope));

		Assert.AreEqual(firstKey, secondKey);
	}

	/// <summary>
	/// Test case: unsupported persisted properties are removed from effective settings while raw settings remain intact.
	/// </summary>
	[TestMethod]
	public async Task ResolverPreservesRawSettings()
	{
		var factory = new TestModelFactory();
		using var locationModel = factory.CreateModel("folder", "Folder", out _);
		var location = new FolderLocation(locationModel.Reference);
		var session = new BrowseSession(new TestBrowseLocationResolver([]), new TestViewSettingsStore());
		await using (session)
		{
			await session.NavigateAsync(location);
			var storedSettings = new BrowseViewSettings(groupPropertyId: "unsupported");
			await session.UpdateViewSettingsAsync(storedSettings);
			var capabilities = new BrowseViewCapabilities([new BrowseViewColumnCapabilities("name", 120, isVisibleByDefault: true)]);

			await ((IViewSettingsSession)session).ApplyViewCapabilitiesAsync(capabilities);

			Assert.AreSame(storedSettings, ((IViewSettingsSession)session).PersistedViewSettings);
			Assert.IsNull(session.ViewSettings.GroupPropertyId);
		}
	}
}

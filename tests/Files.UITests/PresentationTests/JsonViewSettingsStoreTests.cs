// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Browsing;
using Files.Core.ViewSettings;
using Files.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests;

/// <summary>
/// Verifies persistent browse view state behavior.
/// </summary>
[TestClass]
public sealed class JsonViewSettingsStoreTests
{
	/// <summary>
	/// Verifies that view state is written and can be loaded by a new store instance.
	/// </summary>
	[TestMethod]
	public async Task StoresAndReloadsViewState()
	{
		var settingsRoot = Path.Combine(Path.GetTempPath(), $"JsonViewSettingsTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(settingsRoot);
		try
		{
			using (var store = new JsonViewSettingsStore(settingsRoot, TimeSpan.FromHours(1)))
			{
				await store.InitializeAsync();
				await store.SetAsync(HomeLocation.Instance, new BrowseViewSettings(ViewLayoutMode.List, itemSize: 2));
				store.SaveNow();
			}

			Assert.IsTrue(File.Exists(Path.Combine(settingsRoot, "Settings", "view-state.json")));
			using var reloaded = new JsonViewSettingsStore(settingsRoot, TimeSpan.FromHours(1));
			await reloaded.InitializeAsync();
			var settings = await reloaded.GetAsync(HomeLocation.Instance);

			Assert.IsNotNull(settings);
			Assert.AreEqual(ViewLayoutMode.List, settings.LayoutMode);
			Assert.AreEqual(2, settings.ItemSize);
		}
		finally
		{
			if (Directory.Exists(settingsRoot))
			{
				Directory.Delete(settingsRoot, recursive: true);
			}
		}
	}
}

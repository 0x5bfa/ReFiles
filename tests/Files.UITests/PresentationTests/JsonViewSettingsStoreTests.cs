// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
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
		var storage = new InMemorySettingsStorage();
		using (var store = new JsonViewSettingsStore(storage, TimeSpan.FromHours(1)))
		{
			await store.InitializeAsync();
			await store.SetAsync(HomeLocation.Instance, new BrowseViewSettings(ViewLayoutMode.List, itemSize: 2));
			store.SaveNow();
		}

		using var reloaded = new JsonViewSettingsStore(storage, TimeSpan.FromHours(1));
		await reloaded.InitializeAsync();
		var settings = await reloaded.GetAsync(HomeLocation.Instance);

		Assert.IsNotNull(settings);
		Assert.AreEqual(ViewLayoutMode.List, settings.LayoutMode);
		Assert.AreEqual(2, settings.ItemSize);
	}
}

internal sealed class InMemorySettingsStorage : ISettingsStorage
{
	private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

	public Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return Task.FromResult(_documents.GetValueOrDefault(fileName));
	}

	public Task WriteTextAsync(string fileName, string text, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_documents[fileName] = text;

		return Task.CompletedTask;
	}
}

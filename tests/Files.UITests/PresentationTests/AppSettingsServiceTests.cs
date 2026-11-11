// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests;

/// <summary>
/// Verifies application setting persistence behavior.
/// </summary>
[TestClass]
public sealed class AppSettingsServiceTests
{
	/// <summary>
	/// Verifies that missing values use system defaults.
	/// </summary>
	[TestMethod]
	public Task UsesDefaultsWhenSettingsAreMissing()
	{
		var storage = new InMemorySettingsStorage();
		using var service = new AppSettingsService(storage, TimeSpan.FromHours(1));

		Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
		Assert.AreEqual(string.Empty, service.LanguageTag);
		Assert.IsTrue(service.ShowFileExtensions);
		Assert.IsFalse(service.ShowHiddenItems);
		Assert.AreEqual(320d, service.PreviewPaneWidth);
		Assert.IsFalse(service.IsPreviewPaneVisible);
		Assert.AreEqual(Files.Controls.SidebarDisplayMode.Expanded, service.SidebarDisplayMode);

		return Task.CompletedTask;
	}

	/// <summary>
	/// Verifies that setting changes are stored and reported.
	/// </summary>
	[TestMethod]
	public async Task StoresAndReportsSettingChanges()
	{
		var storage = new InMemorySettingsStorage();
		using var service = new AppSettingsService(storage, TimeSpan.FromHours(1));
		var changedProperties = new List<string?>();
		service.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

		service.ThemeMode = AppThemeMode.Dark;
		service.LanguageTag = "en-US";
		service.ShowFileExtensions = false;
		service.ShowHiddenItems = true;
		service.PreviewPaneWidth = 420;
		service.IsPreviewPaneVisible = true;
		service.SidebarDisplayMode = Files.Controls.SidebarDisplayMode.Compact;
		service.SaveNow();

		var settingsJson = await storage.ReadTextAsync("settings.json");
		Assert.IsNotNull(settingsJson);
		Assert.IsTrue(settingsJson.Contains("\"ThemeMode\": \"Dark\"", StringComparison.Ordinal));
		using var reloaded = new AppSettingsService(storage, TimeSpan.FromHours(1));
		Assert.AreEqual(AppThemeMode.Dark, reloaded.ThemeMode);
		Assert.AreEqual("en-US", reloaded.LanguageTag);
		Assert.IsFalse(reloaded.ShowFileExtensions);
		Assert.IsTrue(reloaded.ShowHiddenItems);
		Assert.AreEqual(420d, reloaded.PreviewPaneWidth);
		Assert.IsTrue(reloaded.IsPreviewPaneVisible);
		Assert.AreEqual(Files.Controls.SidebarDisplayMode.Compact, reloaded.SidebarDisplayMode);
		CollectionAssert.AreEqual(
			new[]
			{
				nameof(AppSettingsService.ThemeMode),
				nameof(AppSettingsService.LanguageTag),
				nameof(AppSettingsService.ShowFileExtensions),
				nameof(AppSettingsService.ShowHiddenItems),
				nameof(AppSettingsService.PreviewPaneWidth),
				nameof(AppSettingsService.IsPreviewPaneVisible),
				nameof(AppSettingsService.SidebarDisplayMode)
			},
			changedProperties);
	}

	/// <summary>
	/// Verifies that invalid persisted themes fall back to the system theme.
	/// </summary>
	[TestMethod]
	public async Task FallsBackFromInvalidTheme()
	{
		var storage = new InMemorySettingsStorage();
		await storage.WriteTextAsync("settings.json", "{\"ThemeMode\":\"Unexpected\"}");
		using var service = new AppSettingsService(storage, TimeSpan.FromHours(1));

		Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
	}

	/// <summary>
	/// Verifies that generated setting validation normalizes invalid enum and numeric values.
	/// </summary>
	[TestMethod]
	public void NormalizesInvalidGeneratedValues()
	{
		using var service = new AppSettingsService(new AppSettingsData());

		service.ThemeMode = (AppThemeMode)99;
		service.SidebarDisplayMode = (Files.Controls.SidebarDisplayMode)99;
		service.PreviewPaneWidth = 0;

		Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
		Assert.AreEqual(Files.Controls.SidebarDisplayMode.Expanded, service.SidebarDisplayMode);
		Assert.AreEqual(320d, service.PreviewPaneWidth);

		service.PreviewPaneWidth = double.NaN;

		Assert.AreEqual(320d, service.PreviewPaneWidth);
	}

	/// <summary>
	/// Verifies that pending changes are flushed when the service is disposed.
	/// </summary>
	[TestMethod]
	public Task SavesPendingChangesWhenDisposed()
	{
		var storage = new InMemorySettingsStorage();
		var service = new AppSettingsService(storage, TimeSpan.FromHours(1));
		try
		{
			service.IsPreviewPaneVisible = true;
		}
		finally
		{
			service.Dispose();
		}

		using var reloaded = new AppSettingsService(storage, TimeSpan.FromHours(1));
		Assert.IsTrue(reloaded.IsPreviewPaneVisible);

		return Task.CompletedTask;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using Windows.Storage;
using Windows.Storage.Streams;

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
	public async Task UsesDefaultsWhenSettingsAreMissing()
	{
		var settingsRoot = await CreateSettingsRootAsync();
		try
		{
			using var service = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));

			Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
			Assert.AreEqual(string.Empty, service.LanguageTag);
			Assert.IsTrue(service.ShowFileExtensions);
			Assert.IsFalse(service.ShowHiddenItems);
			Assert.AreEqual(320d, service.PreviewPaneWidth);
			Assert.IsFalse(service.IsPreviewPaneVisible);
			Assert.AreEqual(Files.Controls.SidebarDisplayMode.Expanded, service.SidebarDisplayMode);
		}
		finally
		{
			await DeleteSettingsRootAsync(settingsRoot);
		}
	}

	/// <summary>
	/// Verifies that setting changes are stored and reported.
	/// </summary>
	[TestMethod]
	public async Task StoresAndReportsSettingChanges()
	{
		var settingsRoot = await CreateSettingsRootAsync();
		try
		{
			using var service = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));
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

			var settingsFile = await GetSettingsFileAsync(settingsRoot);
			Assert.IsTrue((await FileIO.ReadTextAsync(settingsFile, UnicodeEncoding.Utf8)).Contains("\"ThemeMode\": \"Dark\"", StringComparison.Ordinal));
			using var reloaded = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));
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
		finally
		{
			await DeleteSettingsRootAsync(settingsRoot);
		}
	}

	/// <summary>
	/// Verifies that invalid persisted themes fall back to the system theme.
	/// </summary>
	[TestMethod]
	public async Task FallsBackFromInvalidTheme()
	{
		var settingsRoot = await CreateSettingsRootAsync();
		try
		{
			var settingsFolder = await settingsRoot.CreateFolderAsync("Settings", CreationCollisionOption.OpenIfExists);
			var settingsFile = await settingsFolder.CreateFileAsync("settings.json", CreationCollisionOption.ReplaceExisting);
			await FileIO.WriteTextAsync(settingsFile, "{\"ThemeMode\":\"Unexpected\"}", UnicodeEncoding.Utf8);
			using var service = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));

			Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
		}
		finally
		{
			await DeleteSettingsRootAsync(settingsRoot);
		}
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
	public async Task SavesPendingChangesWhenDisposed()
	{
		var settingsRoot = await CreateSettingsRootAsync();
		try
		{
			var service = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));
			try
			{
				service.IsPreviewPaneVisible = true;
			}
			finally
			{
				service.Dispose();
			}

			using var reloaded = new AppSettingsService(settingsRoot, TimeSpan.FromHours(1));
			Assert.IsTrue(reloaded.IsPreviewPaneVisible);
		}
		finally
		{
			await DeleteSettingsRootAsync(settingsRoot);
		}
	}

	private static async Task<StorageFolder> CreateSettingsRootAsync()
	{
		var folderName = $"AppSettingsTests-{Guid.NewGuid():N}";
		var directory = Path.Combine(Path.GetTempPath(), folderName);
		Directory.CreateDirectory(directory);
		var settingsRoot = await StorageFolder.GetFolderFromPathAsync(directory);

		return settingsRoot;
	}

	private static async Task<StorageFile> GetSettingsFileAsync(StorageFolder settingsRoot)
	{
		var settingsFolder = await settingsRoot.GetFolderAsync("Settings");

		return await settingsFolder.GetFileAsync("settings.json");
	}

	private static async Task DeleteSettingsRootAsync(StorageFolder settingsRoot)
	{
		await settingsRoot.DeleteAsync(StorageDeleteOption.PermanentDelete);
	}
}

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
	public void UsesDefaultsWhenSettingsAreMissing()
	{
		var service = new AppSettingsService(new Dictionary<string, object>());

		Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
		Assert.AreEqual(string.Empty, service.LanguageTag);
	}

	/// <summary>
	/// Verifies that setting changes are stored and reported.
	/// </summary>
	[TestMethod]
	public void StoresAndReportsSettingChanges()
	{
		var values = new Dictionary<string, object>();
		var service = new AppSettingsService(values);
		var changedProperties = new List<string?>();
		service.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

		service.ThemeMode = AppThemeMode.Dark;
		service.LanguageTag = "en-US";

		Assert.AreEqual("Dark", values["ThemeMode"]);
		Assert.AreEqual("en-US", values["LanguageTag"]);
		CollectionAssert.AreEqual(new[] { nameof(AppSettingsService.ThemeMode), nameof(AppSettingsService.LanguageTag) }, changedProperties);
	}

	/// <summary>
	/// Verifies that invalid persisted themes fall back to the system theme.
	/// </summary>
	[TestMethod]
	public void FallsBackFromInvalidTheme()
	{
		var service = new AppSettingsService(new Dictionary<string, object> { ["ThemeMode"] = "Unexpected" });

		Assert.AreEqual(AppThemeMode.System, service.ThemeMode);
	}
}

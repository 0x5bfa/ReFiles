// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Files.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class GeneralSettingsView : UserControl
{
	private readonly AppSettingsService _settings;

	internal IReadOnlyList<LanguageOption> Languages { get; }

	internal GeneralSettingsView(AppSettingsService settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		Languages =
		[
			new(Strings.UseSystemSetting.GetLocalized(), string.Empty),
			new(Strings.EnglishUnitedStates.GetLocalized(), "en-US"),
		];
		InitializeComponent();
		LanguagePicker.SelectedItem = Languages.FirstOrDefault(option => string.Equals(option.LanguageTag, _settings.LanguageTag, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
		SyncDisplaySettings();
	}

	private void GeneralSettingsView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		_settings.PropertyChanged -= Settings_PropertyChanged;
		_settings.PropertyChanged += Settings_PropertyChanged;
		SyncDisplaySettings();
	}

	private void GeneralSettingsView_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		_settings.PropertyChanged -= Settings_PropertyChanged;
	}

	private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (LanguagePicker.SelectedItem is LanguageOption option)
		{
			_settings.LanguageTag = option.LanguageTag;
		}
	}

	private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is not nameof(AppSettingsService.ShowHiddenItems) and not nameof(AppSettingsService.ShowFileExtensions))
		{
			return;
		}

		if (!DispatcherQueue.HasThreadAccess)
		{
			DispatcherQueue.TryEnqueue(SyncDisplaySettings);

			return;
		}

		SyncDisplaySettings();
	}

	private void ShowFileExtensionsToggleSwitch_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		_settings.ShowFileExtensions = ShowFileExtensionsToggleSwitch.IsOn;
	}

	private void ShowHiddenItemsToggleSwitch_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		_settings.ShowHiddenItems = ShowHiddenItemsToggleSwitch.IsOn;
	}

	private void SyncDisplaySettings()
	{
		ShowHiddenItemsToggleSwitch.IsOn = _settings.ShowHiddenItems;
		ShowFileExtensionsToggleSwitch.IsOn = _settings.ShowFileExtensions;
	}
}

internal sealed record LanguageOption(string Label, string LanguageTag);

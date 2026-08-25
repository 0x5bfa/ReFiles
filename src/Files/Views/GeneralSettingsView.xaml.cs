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

	internal string Title => Strings.General.GetLocalized();

	internal string LanguageLabel => Strings.Language.GetLocalized();

	internal string LanguageDescription => Strings.LanguageDescription.GetLocalized();

	internal string RestartDescription => Strings.RestartToApplyLanguage.GetLocalized();

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
	}

	private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (LanguagePicker.SelectedItem is LanguageOption option)
		{
			_settings.LanguageTag = option.LanguageTag;
		}
	}
}

internal sealed record LanguageOption(string Label, string LanguageTag);

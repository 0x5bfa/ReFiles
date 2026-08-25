// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Files.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class AppearanceSettingsView : UserControl
{
	private readonly AppSettingsService _settings;

	internal IReadOnlyList<ThemeOption> Themes { get; }

	internal string Title => Strings.Appearance.GetLocalized();

	internal string ThemeLabel => Strings.Theme.GetLocalized();

	internal string ThemeDescription => Strings.ThemeDescription.GetLocalized();

	internal AppearanceSettingsView(AppSettingsService settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		Themes =
		[
			new(Strings.UseSystemSetting.GetLocalized(), AppThemeMode.System),
			new(Strings.Light.GetLocalized(), AppThemeMode.Light),
			new(Strings.Dark.GetLocalized(), AppThemeMode.Dark),
		];
		InitializeComponent();
		ThemePicker.SelectedItem = Themes.First(option => option.Value == _settings.ThemeMode);
	}

	private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ThemePicker.SelectedItem is ThemeOption option)
		{
			_settings.ThemeMode = option.Value;
		}
	}
}

internal sealed record ThemeOption(string Label, AppThemeMode Value);

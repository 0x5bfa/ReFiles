// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using Windows.Globalization;
using Windows.Storage;

namespace Files.Settings;

internal sealed class AppSettingsService : INotifyPropertyChanged
{
	private const string ContainerName = "Application";
	private const string LanguageTagKey = "LanguageTag";
	private const string ThemeModeKey = "ThemeMode";

	private readonly IDictionary<string, object> _values;

	public string LanguageTag
	{
		get => GetString(LanguageTagKey);
		set => SetString(LanguageTagKey, value ?? string.Empty, nameof(LanguageTag));
	}

	public AppThemeMode ThemeMode
	{
		get => Enum.TryParse<AppThemeMode>(GetString(ThemeModeKey), out var value) && Enum.IsDefined(value) ? value : AppThemeMode.System;
		set => SetString(ThemeModeKey, value.ToString(), nameof(ThemeMode));
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public AppSettingsService()
		: this(ApplicationData.Current.LocalSettings.CreateContainer(ContainerName, ApplicationDataCreateDisposition.Always).Values)
	{
	}

	internal AppSettingsService(IDictionary<string, object> values)
	{
		ArgumentNullException.ThrowIfNull(values);

		_values = values;
	}

	public void ApplyLanguage() => ApplicationLanguages.PrimaryLanguageOverride = LanguageTag;

	private string GetString(string key) => _values.TryGetValue(key, out var value) && value is string text ? text : string.Empty;

	private void SetString(string key, string value, string propertyName)
	{
		if (string.Equals(GetString(key), value, StringComparison.Ordinal))
		{
			return;
		}

		_values[key] = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

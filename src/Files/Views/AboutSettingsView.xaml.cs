// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.System;

namespace Files.Views;

public sealed partial class AboutSettingsView : UserControl
{
	private const string SourceCodeUrl = "https://github.com/0x5bfa/ReFiles";

	internal string Title => Strings.About.GetLocalized();

	internal string AppName { get; }

	internal string VersionDescription { get; }

	internal string SourceCodeLabel => Strings.SourceCode.GetLocalized();

	internal string SourceCodeDescription => Strings.SourceCodeDescription.GetLocalized();

	internal AboutSettingsView()
	{
		(AppName, VersionDescription) = GetAppIdentity();
		InitializeComponent();
	}

	private static (string AppName, string VersionDescription) GetAppIdentity()
	{
		try
		{
			var package = Package.Current;
			var version = package.Id.Version;

			return (package.DisplayName, $"{Strings.AppVersion.GetLocalized()} {version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
		}
		catch (InvalidOperationException)
		{
			var assembly = Assembly.GetEntryAssembly() ?? typeof(AboutSettingsView).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";

			return (assembly.GetName().Name ?? "Files", $"{Strings.AppVersion.GetLocalized()} {version}");
		}
	}

	private async void OpenSourceCode_Click(object sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri(SourceCodeUrl));
}

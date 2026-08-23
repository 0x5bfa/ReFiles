// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class CompatibilityPropertyView : UserControl
{
	private readonly Action<string, string?> _launchSystemTool;

	internal WindowsShellCompatibilityProperties Compatibility { get; }

	internal IReadOnlyList<string> CompatibilityVersions { get; }

	internal string ChangeHighDpiSettingsLabel => Strings.ChangeHighDpiSettings.GetLocalized();

	internal string CompatibilityModeLabel => Strings.CompatibilityMode.GetLocalized();

	internal string DisableFullscreenOptimizationsLabel => Strings.DisableFullscreenOptimizations.GetLocalized();

	internal string Intro => Strings.CompatibilityIntro.GetLocalized();

	internal string ProgramLabel => Strings.Program.GetLocalized();

	internal string ReducedColorModeLabel => Strings.ReducedColorMode.GetLocalized();

	internal string RunAsAdministratorLabel => Strings.RunAsAdministrator.GetLocalized();

	internal string RunIn640By480Label => Strings.RunIn640By480.GetLocalized();

	internal string RunInCompatibilityModeLabel => Strings.RunInCompatibilityMode.GetLocalized();

	internal string SettingsLabel => Strings.CompatibilitySettings.GetLocalized();

	internal string TroubleshooterLabel => Strings.RunCompatibilityTroubleshooter.GetLocalized();

	internal CompatibilityPropertyView(WindowsShellCompatibilityProperties compatibility, Action<string, string?> launchSystemTool)
	{
		Compatibility = compatibility;
		_launchSystemTool = launchSystemTool;
		CompatibilityVersions =
		[
			Strings.Windows8.GetLocalized(),
			Strings.Windows7.GetLocalized(),
			Strings.WindowsVistaServicePack2.GetLocalized(),
			Strings.WindowsVistaServicePack1.GetLocalized(),
			Strings.WindowsVista.GetLocalized(),
			Strings.WindowsXpServicePack3.GetLocalized(),
		];
		InitializeComponent();
	}

	private void TroubleshooterButton_Click(object sender, RoutedEventArgs e)
	{
		var encodedExecutablePath = Uri.EscapeDataString(Compatibility.ExecutablePath);
		_launchSystemTool($"ms-contact-support://compattroubleshooter/InvocationContextMenu/?Product=Windows&dialog_ExePath={encodedExecutablePath}", null);
	}

	private void HighDpiButton_Click(object sender, RoutedEventArgs e)
	{
		_launchSystemTool("ms-settings:display-advanced", null);
	}
}

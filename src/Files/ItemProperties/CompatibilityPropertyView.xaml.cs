// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class CompatibilityPropertyView : UserControl
{
	private readonly Action<string, string?> _launchSystemTool;

	internal WindowsShellCompatibilityProperties Compatibility { get; }

	internal IReadOnlyList<string> CompatibilityVersions { get; }

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

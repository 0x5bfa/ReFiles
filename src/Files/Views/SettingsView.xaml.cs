// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class SettingsView : UserControl
{
	public string Title => Strings.Settings.GetLocalized();

	public SettingsView()
	{
		InitializeComponent();
	}
}

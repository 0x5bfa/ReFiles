// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;

namespace Files.ControlTests;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		MainWindow.Instance.Activate();
	}
}

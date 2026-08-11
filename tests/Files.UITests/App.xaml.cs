// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.UI.Xaml;
using System;

namespace Files.UITests
{
	public partial class App : Application
	{
		public App()
		{
			InitializeComponent();
		}

		protected override void OnLaunched(LaunchActivatedEventArgs args)
		{
			Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.CreateDefaultUI();

			var window = MainWindow.Instance;
			window.Activate();

			UITestMethodAttribute.DispatcherQueue = window.DispatcherQueue;

			Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.Run(Environment.CommandLine);
		}
	}
}

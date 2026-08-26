// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System;

namespace Files.UITests
{
	public partial class UnitTestApp : Application
	{
		private Window? _window;

		internal static DispatcherQueue TestDispatcherQueue { get; private set; } = null!;

		public UnitTestApp()
		{
			InitializeComponent();
		}

		protected override void OnLaunched(LaunchActivatedEventArgs args)
		{
			Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.CreateDefaultUI();

			_window = new UnitTestAppWindow();
			_window.Activate();
			TestDispatcherQueue = _window.DispatcherQueue;
			UITestMethodAttribute.DispatcherQueue = TestDispatcherQueue;

			Microsoft.VisualStudio.TestPlatform.TestExecutor.UnitTestClient.Run(Environment.CommandLine);
		}
	}
}

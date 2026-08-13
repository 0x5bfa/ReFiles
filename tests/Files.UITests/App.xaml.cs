// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Testing.Platform.Builder;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System;
using System.Linq;

namespace Files.UITests
{
	public partial class App : Application
	{
		private Window? _window;

		internal static DispatcherQueue TestDispatcherQueue { get; private set; } = null!;

		public App()
		{
			InitializeComponent();
		}

		protected override async void OnLaunched(LaunchActivatedEventArgs args)
		{
			var exitCode = 1;
			try
			{
				_window = new Window();
				_window.Activate();
				TestDispatcherQueue = _window.DispatcherQueue;
				UITestMethodAttribute.DispatcherQueue = TestDispatcherQueue;
				Console.WriteLine("Starting self-hosted WinUI tests.");

				var commandLineArguments = Environment.GetCommandLineArgs().Skip(1).Where(static argument => !argument.Contains("EnableMSTestRunner", StringComparison.Ordinal)).ToArray();
				var builder = await TestApplication.CreateBuilderAsync(commandLineArguments);
				builder.AddSelfRegisteredExtensions(commandLineArguments);
				using var testApplication = await builder.BuildAsync();
				exitCode = await testApplication.RunAsync();
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine(exception);
			}
			finally
			{
				Environment.ExitCode = exitCode;
				_window?.Close();
				Exit();
				Environment.Exit(exitCode);
			}
		}
	}
}

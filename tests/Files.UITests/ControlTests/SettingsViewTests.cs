// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Settings;
using Files.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.ControlTests;

[TestClass]
public sealed class SettingsViewTests
{
	/// <summary>
	/// Verifies that settings option collections can cross the WinRT ItemsSource boundary.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task OptionCollectionsLoadAsItemsSources()
	{
		var settings = new AppSettingsService(new Dictionary<string, object>());
		var settingsView = new SettingsView(settings);
		var appearanceView = new AppearanceSettingsView(settings);
		var content = new StackPanel();
		content.Children.Add(settingsView);
		content.Children.Add(appearanceView);
		var window = new Window { Content = content };
		try
		{
			var loaded = Task.WhenAll(WaitForLoadedAsync(settingsView), WaitForLoadedAsync(appearanceView));
			window.Activate();
			await loaded;

			var themePicker = Assert.IsInstanceOfType<ComboBox>(appearanceView.FindName("ThemePicker"));
			Assert.AreSame(appearanceView.Themes, themePicker.ItemsSource);
			Assert.HasCount(2, settingsView.NavigationItems);
			Assert.HasCount(1, settingsView.FooterNavigationItems);
		}
		finally
		{
			window.Content = null;
			window.Close();
		}
	}

	private static Task WaitForLoadedAsync(FrameworkElement element)
	{
		if (element.IsLoaded)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		RoutedEventHandler? handler = null;
		handler = (_, _) =>
		{
			element.Loaded -= handler;
			completion.SetResult();
		};
		element.Loaded += handler;

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}
}

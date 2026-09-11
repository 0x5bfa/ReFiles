// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Controls;
using Files.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.ControlTests;

/// <summary>Verifies Omnibar state and template behavior in a live WinUI visual tree.</summary>
[TestClass]
[DoNotParallelize]
public sealed class OmnibarTests
{
	/// <summary>Verifies that suggestion previews do not replace the draft for a mode.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task SwitchingModesRestoresDraftAfterSuggestionPreview()
	{
		var pathMode = new OmnibarMode { IsDefault = true, ModeName = "Path", ItemsSource = new ObservableCollection<string> { "preview" } };
		var paletteMode = new OmnibarMode { ModeName = "Palette" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { pathMode, paletteMode }, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			var modeButton = GetNamedDescendant<Button>(pathMode, "PART_ModeButton");
			Assert.AreEqual(modeButton.ActualWidth, omnibar.AutoSuggestBoxPadding.Left, 0.5);
			pathMode.Text = "draft";
			await WaitForDispatcherAsync();
			Assert.AreSame(pathMode, omnibar.CurrentSelectedMode);
			Assert.AreEqual("draft", pathMode.Text);

			omnibar.ChooseSuggestionItem("preview", true);
			Assert.AreEqual("preview", textBox.Text);

			omnibar.CurrentSelectedMode = paletteMode;
			omnibar.CurrentSelectedMode = pathMode;
			await WaitForDispatcherAsync();

			Assert.AreEqual("draft", textBox.Text);
			Assert.AreEqual("draft", pathMode.Text);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that mouse and arrow-key suggestion update policies are independent.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task SuggestionUpdatePoliciesAreIndependent()
	{
		var mode = new OmnibarMode { IsDefault = true, ModeName = "Search", UpdateTextOnSelect = false, UpdateTextOnArrowKeys = true };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { mode }, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			textBox.Text = "draft";
			await WaitForDispatcherAsync();

			omnibar.ChooseSuggestionItem("mouse", false);
			Assert.AreEqual("draft", textBox.Text);

			omnibar.ChooseSuggestionItem("arrow", true);
			Assert.AreEqual("arrow", textBox.Text);

			mode.UpdateTextOnSelect = true;
			mode.UpdateTextOnArrowKeys = false;
			omnibar.ChooseSuggestionItem("arrow-unchanged", true);
			Assert.AreEqual("arrow", textBox.Text);

			omnibar.ChooseSuggestionItem("mouse-updated", false);
			Assert.AreEqual("mouse-updated", textBox.Text);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that inactive modes show their placeholder unless they provide alternate content.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task InactiveModeShowsPlaceholderWithoutAlternateContent()
	{
		var pathMode = new OmnibarMode { IsDefault = true, ModeName = "Path", ContentOnInactive = new Border() };
		var searchMode = new OmnibarMode { ModeName = "Search", PlaceholderText = "Search" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { pathMode, searchMode }, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			var inputArea = GetNamedDescendant<Grid>(textBox, "PART_TextBoxInputArea");
			Assert.AreEqual(Visibility.Collapsed, inputArea.Visibility);

			omnibar.CurrentSelectedMode = searchMode;
			await WaitForDispatcherAsync();

			Assert.AreEqual("Search", textBox.PlaceholderText);
			Assert.AreEqual(Visibility.Visible, inputArea.Visibility);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that a single inactive mode still exposes its placeholder after the template is applied.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task SingleModeShowsPlaceholderAfterTemplateLoad()
	{
		var searchMode = new OmnibarMode { IsDefault = true, ModeName = "Search", PlaceholderText = "Search" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { searchMode }, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			var inputArea = GetNamedDescendant<Grid>(textBox, "PART_TextBoxInputArea");
			var placeholder = GetNamedDescendant<TextBlock>(textBox, "PlaceholderTextContentPresenter");
			Assert.AreEqual("Search", textBox.PlaceholderText);
			Assert.AreEqual(Visibility.Visible, inputArea.Visibility);
			Assert.AreEqual("Search", placeholder.Text);
			Assert.AreEqual(Visibility.Visible, placeholder.Visibility);
			Assert.IsTrue(placeholder.ActualWidth > 0);
			Assert.IsTrue(placeholder.ActualHeight > 0);

			searchMode.PlaceholderText = "Find";
			await WaitForDispatcherAsync();
			Assert.AreEqual("Find", textBox.PlaceholderText);

			searchMode.ContentOnInactive = new Border();
			await WaitForDispatcherAsync();
			Assert.AreEqual(Visibility.Collapsed, inputArea.Visibility);

			searchMode.ContentOnInactive = null;
			await WaitForDispatcherAsync();
			Assert.AreEqual(Visibility.Visible, inputArea.Visibility);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that the SearchOmnibar declared by NavigationToolbar keeps its placeholder visible.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task NavigationToolbarSearchOmnibarShowsPlaceholder()
	{
		var toolbar = new NavigationToolbar { Width = 900, Height = 60 };
		var window = new Window { Content = toolbar };

		try
		{
			var loaded = WaitForLoadedAsync(toolbar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var searchOmnibar = GetNamedDescendant<Omnibar>(toolbar, "SearchOmnibar");
			var textBox = GetNamedDescendant<TextBox>(searchOmnibar, "PART_TextBox");
			var inputArea = GetNamedDescendant<Grid>(textBox, "PART_TextBoxInputArea");
			var placeholder = GetNamedDescendant<TextBlock>(textBox, "PlaceholderTextContentPresenter");
			Assert.AreEqual("Search", textBox.PlaceholderText);
			Assert.AreEqual(Visibility.Visible, inputArea.Visibility);
			Assert.AreEqual("Search", placeholder.Text);
			Assert.IsNotNull(placeholder.Foreground);
			Assert.IsTrue(placeholder.ActualWidth > 0);
		}
		finally
		{
			await CloseWindowAsync(window, toolbar);
		}
	}

	/// <summary>Verifies that shutdown preparation allows programmatic focus to leave an Omnibar text box.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task ShutdownPreparationAllowsProgrammaticFocusToLeaveTextBox()
	{
		var mode = new OmnibarMode { IsDefault = true, ModeName = "Search" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { mode }, Width = 480 };
		var target = new Button { Content = "Target" };
		var host = new StackPanel { Children = { omnibar, target } };
		var window = new Window { Content = host };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			omnibar.FocusTextBox();
			await WaitForDispatcherAsync();
			Assert.AreSame(textBox, FocusManager.GetFocusedElement(host.XamlRoot));

			omnibar.PrepareForShutdown();
			Assert.IsTrue(target.Focus(FocusState.Programmatic));
			await WaitForDispatcherAsync();

			Assert.AreSame(target, FocusManager.GetFocusedElement(host.XamlRoot));
		}
		finally
		{
			await CloseWindowAsync(window, host);
		}
	}

	/// <summary>Verifies that programmatic changes to an inactive mode are applied when it becomes active.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task InactiveModeTextChangesAreAppliedWhenSelected()
	{
		var pathMode = new OmnibarMode { IsDefault = true, ModeName = "Path" };
		var searchMode = new OmnibarMode { ModeName = "Search", Text = "initial" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { pathMode, searchMode }, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			omnibar.CurrentSelectedMode = searchMode;
			omnibar.CurrentSelectedMode = pathMode;
			searchMode.Text = "updated while inactive";
			omnibar.CurrentSelectedMode = searchMode;
			await WaitForDispatcherAsync();

			Assert.AreEqual("updated while inactive", textBox.Text);
			Assert.AreEqual("updated while inactive", searchMode.Text);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that reloading a template preserves the selected mode and input state.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	[DoNotParallelize]
	public async Task ReloadingTemplatePreservesModeAndInput()
	{
		var mode = new OmnibarMode { IsDefault = true, ModeName = "Search" };
		var omnibar = new Omnibar { Modes = new ObservableCollection<OmnibarMode> { mode }, Width = 480 };
		var host = new Grid();
		host.Children.Add(omnibar);
		var window = new Window { Content = host };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var textBox = GetNamedDescendant<TextBox>(omnibar, "PART_TextBox");
			mode.Text = "draft";
			await WaitForDispatcherAsync();

			for (var iteration = 0; iteration < 3; iteration++)
			{
				var unloaded = WaitForUnloadedAsync(omnibar);
				host.Children.Remove(omnibar);
				await unloaded;
				if (iteration is 0)
				{
					mode.Text = "updated while unloaded";
				}

				var reloaded = WaitForLoadedAsync(omnibar);
				host.Children.Add(omnibar);
				await reloaded;
				await WaitForDispatcherAsync();
			}

			Assert.AreSame(mode, omnibar.CurrentSelectedMode);
			Assert.AreEqual("updated while unloaded", textBox.Text);
			Assert.AreEqual("updated while unloaded", mode.Text);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that observable mode changes rebuild the host consistently and recover selection.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task ObservableModesRebuildIdempotently()
	{
		var pathMode = new OmnibarMode { IsDefault = true, ModeName = "Path" };
		var paletteMode = new OmnibarMode { ModeName = "Palette" };
		var searchMode = new OmnibarMode { ModeName = "Search" };
		var modes = new ObservableCollection<OmnibarMode> { pathMode, paletteMode };
		var omnibar = new Omnibar { Modes = modes, Width = 480 };
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var modesHost = GetNamedDescendant<Grid>(omnibar, "PART_ModesHostGrid");
			Assert.AreSame(pathMode, omnibar.CurrentSelectedMode);
			Assert.AreEqual(3, modesHost.Children.Count);
			Assert.AreEqual(3, modesHost.ColumnDefinitions.Count);

			modes.Add(searchMode);
			await WaitForDispatcherAsync();
			Assert.AreEqual(5, modesHost.Children.Count);
			Assert.AreEqual(5, modesHost.ColumnDefinitions.Count);

			modes.Remove(pathMode);
			await WaitForDispatcherAsync();
			Assert.AreSame(paletteMode, omnibar.CurrentSelectedMode);
			Assert.AreEqual(3, modesHost.Children.Count);
			Assert.AreEqual(3, modesHost.ColumnDefinitions.Count);

			omnibar.Modes = null;
			await WaitForDispatcherAsync();
			Assert.IsNull(omnibar.CurrentSelectedMode);
			Assert.AreEqual(0, modesHost.Children.Count);
			Assert.AreEqual(0, modesHost.ColumnDefinitions.Count);

			omnibar.Modes = modes;
			await WaitForDispatcherAsync();
			Assert.AreSame(paletteMode, omnibar.CurrentSelectedMode);
			Assert.AreEqual(3, modesHost.Children.Count);
			Assert.AreEqual(3, modesHost.ColumnDefinitions.Count);

		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	/// <summary>Verifies that a mode name selects the mode before the control template is applied.</summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task CurrentSelectedModeNameSelectsModeBeforeTemplateLoad()
	{
		var pathMode = new OmnibarMode { IsDefault = true, ModeName = "Path" };
		var searchMode = new OmnibarMode { ModeName = "Search" };
		var omnibar = new Omnibar { Width = 480 };
		omnibar.Modes = new ObservableCollection<OmnibarMode> { pathMode, searchMode };
		omnibar.CurrentSelectedModeName = "Search";
		var window = new Window { Content = omnibar };

		try
		{
			var loaded = WaitForLoadedAsync(omnibar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			Assert.AreSame(searchMode, omnibar.CurrentSelectedMode);
			Assert.AreEqual("Search", omnibar.CurrentSelectedModeName);
			Assert.AreEqual("Search", searchMode.ModeName);
		}
		finally
		{
			await CloseWindowAsync(window, omnibar);
		}
	}

	private static async Task WaitForDispatcherAsync()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.IsTrue(UnitTestApp.TestDispatcherQueue.TryEnqueue(completion.SetResult));
		await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

	private static Task WaitForUnloadedAsync(FrameworkElement element)
	{
		if (!element.IsLoaded)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		RoutedEventHandler? handler = null;
		handler = (_, _) =>
		{
			element.Unloaded -= handler;
			completion.SetResult();
		};
		element.Unloaded += handler;

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static async Task CloseWindowAsync(Window window, FrameworkElement root)
	{
		var unloaded = root.IsLoaded ? WaitForUnloadedAsync(root) : Task.CompletedTask;
		window.Content = null;
		await unloaded;
		window.Close();
		await WaitForDispatcherAsync();
	}

	private static T GetNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
	{
		var descendant = FindNamedDescendant<T>(root, name);
		Assert.IsNotNull(descendant);

		return descendant;
	}

	private static T? FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
	{
		var childCount = VisualTreeHelper.GetChildrenCount(root);
		for (var index = 0; index < childCount; index++)
		{
			var child = VisualTreeHelper.GetChild(root, index);
			if (child is T candidate && candidate.Name == name)
			{
				return candidate;
			}

			var descendant = FindNamedDescendant<T>(child, name);
			if (descendant is not null)
			{
				return descendant;
			}
		}

		return null;
	}

}

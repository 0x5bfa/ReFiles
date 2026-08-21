// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Controls;
using Files.Controls.Primitives;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Files.UITests.ControlTests;

/// <summary>
/// Verifies toolbar materialization and overflow behavior in a live WinUI visual tree.
/// </summary>
[TestClass]
public sealed class ToolbarTests
{
	/// <summary>
	/// Verifies that navigation-style unloads and width changes do not replace materialized toolbar controls.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task MaterializedItemsRemainStableAcrossReloadAndOverflowChanges()
	{
		var items = new ObservableCollection<ToolbarItem>
		{
			new() { ItemType = ToolbarItemTypes.Button, Label = "Back", OverflowBehavior = OverflowBehaviors.Never },
			new() { ItemType = ToolbarItemTypes.Button, Label = "Copy", OverflowBehavior = OverflowBehaviors.Auto },
			new() { ItemType = ToolbarItemTypes.Button, Label = "Cut", OverflowBehavior = OverflowBehaviors.Auto },
			new() { ItemType = ToolbarItemTypes.ToggleButton, Label = "View", OverflowBehavior = OverflowBehaviors.Auto },
		};
		var toolbar = new Toolbar { HorizontalAlignment = HorizontalAlignment.Left, Items = items, Width = 480 };
		var host = new Grid();
		host.Children.Add(toolbar);
		var window = new Window { Content = host };
		try
		{
			var loaded = WaitForLoadedAsync(toolbar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var itemsPanel = GetNamedDescendant<ToolbarItemsPanel>(toolbar, Toolbar.ToolbarItemsPanelPartName);
			var overflowPanel = GetNamedDescendant<StackPanel>(toolbar, Toolbar.OverflowStackPanelPartName);
			var materializedHosts = itemsPanel.Children.Cast<UIElement>().ToArray();
			var materializedControls = materializedHosts.Cast<ContentPresenter>().Select(static hostPresenter => hostPresenter.Content).ToArray();
			Assert.AreEqual(items.Count, materializedHosts.Length);

			for (var iteration = 0; iteration < 128; iteration++)
			{
				var unloaded = WaitForUnloadedAsync(toolbar);
				host.Children.Remove(toolbar);
				await unloaded;

				items[1].IsEnabled = iteration % 2 == 0;
				toolbar.Width = iteration % 2 == 0 ? 96 : 480;

				loaded = WaitForLoadedAsync(toolbar);
				host.Children.Add(toolbar);
				await loaded;
				await WaitForDispatcherAsync();

				Assert.AreSame(itemsPanel, GetNamedDescendant<ToolbarItemsPanel>(toolbar, Toolbar.ToolbarItemsPanelPartName));
				Assert.AreEqual(materializedHosts.Length, itemsPanel.Children.Count);
				for (var itemIndex = 0; itemIndex < materializedHosts.Length; itemIndex++)
				{
					Assert.AreSame(materializedHosts[itemIndex], itemsPanel.Children[itemIndex]);
					Assert.AreSame(materializedControls[itemIndex], ((ContentPresenter)itemsPanel.Children[itemIndex]).Content);
				}

				var visibleHostCount = materializedHosts.Cast<FrameworkElement>().Count(static materializedHost => materializedHost.Opacity > 0);
				Assert.AreEqual(iteration % 2 == 0 ? 1 : items.Count, visibleHostCount);
				Assert.IsTrue(materializedHosts.Cast<FrameworkElement>().All(static materializedHost => materializedHost.Visibility == Visibility.Visible));
				Assert.AreEqual(iteration % 2 == 0 ? Visibility.Visible : Visibility.Collapsed, overflowPanel.Visibility);
			}
		}
		finally
		{
			window.Close();
		}
	}

	private static async Task WaitForDispatcherAsync()
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.IsTrue(App.TestDispatcherQueue.TryEnqueue(completion.SetResult));
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

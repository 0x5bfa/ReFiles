// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Controls;
using Files.Controls.Primitives;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
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

	/// <summary>
	/// Verifies that a themed icon follows its button's enabled state after the toolbar is reloaded.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task ThemedIconFollowsButtonStateAcrossReload()
	{
		if (Application.Current.Resources.TryGetValue("ThemedIconHighContrast", out var highContrastValue) && highContrastValue is true)
		{
			Assert.Inconclusive("The enabled layered variant is overridden while the system is using high contrast.");
		}

		var iconData = new ThemedIconData { OutlineData = "M0,0 H16 V16 H0 Z", Size = 16 };
		iconData.Layers.Add(new ThemedIconLayer { LayerType = ThemedIconLayerType.Base, PathData = "M0,0 H8 V16 H0 Z" });
		iconData.Layers.Add(new ThemedIconLayer { LayerType = ThemedIconLayerType.Accent, PathData = "M8,0 H16 V16 H8 Z" });
		var item = new ToolbarItem { ItemType = ToolbarItemTypes.Button, IsEnabled = false, Label = "Copy", ThemedIcon = iconData };
		var toolbar = new Toolbar { HorizontalAlignment = HorizontalAlignment.Left, Items = new ObservableCollection<ToolbarItem> { item }, Width = 480 };
		var host = new Grid();
		host.Children.Add(toolbar);
		var window = new Window { Content = host };
		try
		{
			var loaded = WaitForLoadedAsync(toolbar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			foreach (var expectedIsEnabled in new[] { false, true, false, true })
			{
				if (item.IsEnabled != expectedIsEnabled)
				{
					var unloaded = WaitForUnloadedAsync(toolbar);
					host.Children.Remove(toolbar);
					await unloaded;

					item.IsEnabled = expectedIsEnabled;
					loaded = WaitForLoadedAsync(toolbar);
					host.Children.Add(toolbar);
					await loaded;
					await WaitForDispatcherAsync();
				}

				var itemsPanel = GetNamedDescendant<ToolbarItemsPanel>(toolbar, Toolbar.ToolbarItemsPanelPartName);
				var button = ((ContentPresenter)itemsPanel.Children[0]).Content as ToolbarButton;
				Assert.IsNotNull(button);
				Assert.AreEqual(expectedIsEnabled, button.IsEnabled);
				var icon = GetNamedDescendant<ThemedIcon>(button, "PART_ThemedIcon");
				Assert.IsTrue(icon.IsEnabled, "The icon's local state must remain available for owner state tracking.");
				Assert.AreEqual(expectedIsEnabled ? 2 : 1, GetRenderedShapeCount(icon));
			}
		}
		finally
		{
			window.Close();
		}
	}

	/// <summary>
	/// Verifies that hidden items do not affect overflow and that overflow menu state and separators are normalized.
	/// </summary>
	/// <returns>A task that represents the asynchronous test operation.</returns>
	[UITestMethod]
	public async Task VisibilityAndOverflowMenuStateRemainConsistent()
	{
		var hiddenItem = new ToolbarItem { ItemType = ToolbarItemTypes.Button, IsVisible = false, Label = "Hidden", OverflowBehavior = OverflowBehaviors.Always };
		var disabledItem = new ToolbarItem { ItemType = ToolbarItemTypes.Button, IsEnabled = false, Label = "Disabled", OverflowBehavior = OverflowBehaviors.Always };
		var enabledItem = new ToolbarItem { ItemType = ToolbarItemTypes.Button, Label = "Enabled", OverflowBehavior = OverflowBehaviors.Always };
		var items = new ObservableCollection<ToolbarItem>
		{
			new() { ItemType = ToolbarItemTypes.Button, Label = "Back", OverflowBehavior = OverflowBehaviors.Never },
			hiddenItem,
			new() { ItemType = ToolbarItemTypes.Separator, OverflowBehavior = OverflowBehaviors.Always },
			disabledItem,
			new() { ItemType = ToolbarItemTypes.Separator, OverflowBehavior = OverflowBehaviors.Always },
			new() { ItemType = ToolbarItemTypes.Separator, OverflowBehavior = OverflowBehaviors.Always },
			enabledItem,
			new() { ItemType = ToolbarItemTypes.Separator, OverflowBehavior = OverflowBehaviors.Always },
		};
		var toolbar = new Toolbar { HorizontalAlignment = HorizontalAlignment.Left, Items = items, Width = 480 };
		var window = new Window { Content = toolbar };
		try
		{
			var loaded = WaitForLoadedAsync(toolbar);
			window.Activate();
			await loaded;
			await WaitForDispatcherAsync();

			var overflowPanel = GetNamedDescendant<StackPanel>(toolbar, Toolbar.OverflowStackPanelPartName);
			Assert.AreEqual(Visibility.Visible, overflowPanel.Visibility);
			var overflowButton = GetNamedDescendant<ToolbarFlyoutButton>(toolbar, Toolbar.OverflowButtonPartName);
			var overflowFlyout = overflowButton.Flyout as MenuFlyout;
			Assert.IsNotNull(overflowFlyout);
			overflowFlyout.ShowAt(overflowButton);
			await WaitForDispatcherAsync();

			Assert.AreEqual(3, overflowFlyout.Items.Count);
			Assert.IsInstanceOfType<MenuFlyoutItem>(overflowFlyout.Items[0]);
			Assert.AreEqual("Disabled", ((MenuFlyoutItem)overflowFlyout.Items[0]).Text);
			Assert.IsFalse(((MenuFlyoutItem)overflowFlyout.Items[0]).IsEnabled);
			Assert.IsInstanceOfType<MenuFlyoutSeparator>(overflowFlyout.Items[1]);
			Assert.IsInstanceOfType<MenuFlyoutItem>(overflowFlyout.Items[2]);
			Assert.AreEqual("Enabled", ((MenuFlyoutItem)overflowFlyout.Items[2]).Text);
			Assert.IsTrue(((MenuFlyoutItem)overflowFlyout.Items[2]).IsEnabled);
			overflowFlyout.Hide();

			disabledItem.IsVisible = false;
			enabledItem.IsVisible = false;
			toolbar.UpdateLayout();
			await WaitForDispatcherAsync();
			Assert.AreEqual(Visibility.Collapsed, overflowPanel.Visibility);

			hiddenItem.IsVisible = true;
			toolbar.UpdateLayout();
			await WaitForDispatcherAsync();
			Assert.AreEqual(Visibility.Visible, overflowPanel.Visibility);
		}
		finally
		{
			window.Close();
		}
	}

	private static int GetRenderedShapeCount(ThemedIcon icon)
	{
		var visualSource = icon.Source as ThemedIconVisualSource;
		Assert.IsNotNull(visualSource);
		var compositor = ElementCompositionPreview.GetElementVisual(icon).Compositor;
		using var animatedVisual = visualSource.TryCreateAnimatedVisual(compositor, out var diagnostics);
		Assert.IsNotNull(animatedVisual, diagnostics?.ToString());
		var rootVisual = animatedVisual.RootVisual as ShapeVisual;
		Assert.IsNotNull(rootVisual);

		return rootVisual.Shapes.Count;
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

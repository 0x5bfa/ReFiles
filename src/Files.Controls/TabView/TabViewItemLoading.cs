// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Controls;

/// <summary>
/// Connects a loading state to the loading visual state in the rounded tab template.
/// </summary>
public static class TabViewItemLoading
{
	/// <summary>Identifies the attached loading state property.</summary>
	public static readonly DependencyProperty IsLoadingProperty =
		DependencyProperty.RegisterAttached("IsLoading", typeof(bool), typeof(TabViewItemLoading), new PropertyMetadata(false, IsLoadingChanged));

	/// <summary>Gets whether the tab should display its loading indicator.</summary>
	/// <param name="element">The tab item.</param>
	/// <returns><see langword="true"/> when the loading visual state is active.</returns>
	public static bool GetIsLoading(DependencyObject element)
	{
		ArgumentNullException.ThrowIfNull(element);

		return (bool)element.GetValue(IsLoadingProperty);
	}

	/// <summary>Sets whether the tab should display its loading indicator.</summary>
	/// <param name="element">The tab item.</param>
	/// <param name="value">Whether the loading visual state is active.</param>
	public static void SetIsLoading(DependencyObject element, bool value)
	{
		ArgumentNullException.ThrowIfNull(element);

		element.SetValue(IsLoadingProperty, value);
	}

	private static void IsLoadingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		if (sender is not TabViewItem tabItem)
		{
			return;
		}

		tabItem.Loaded -= TabItem_Loaded;
		tabItem.Loaded += TabItem_Loaded;
		UpdateVisualState(tabItem);
	}

	private static void TabItem_Loaded(object sender, RoutedEventArgs args)
	{
		if (sender is TabViewItem tabItem)
		{
			UpdateVisualState(tabItem);
		}
	}

	private static void UpdateVisualState(TabViewItem tabItem)
	{
		VisualStateManager.GoToState(tabItem, GetIsLoading(tabItem) ? "Loading" : "NotLoading", true);
	}
}

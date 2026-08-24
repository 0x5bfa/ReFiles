// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Files.Core.Storage;
using Files.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using System.Collections.ObjectModel;

namespace Files.UITests;

/// <summary>
/// Verifies navigation item presentation behavior.
/// </summary>
[TestClass]
public sealed class NavigationItemViewModelTests
{
	/// <summary>
	/// Verifies that sidebar items accept arbitrary data and child sources without a model contract.
	/// </summary>
	[UITestMethod]
	public void AcceptsUncontractedSidebarData()
	{
		var data = new object();
		var children = new ObservableCollection<object> { new() };
		var item = new SidebarItem { Item = data, MenuItemsSource = children };

		Assert.AreSame(data, item.Item);
		Assert.AreSame(children, item.MenuItemsSource);
		Assert.IsTrue(item.HasChildren);
	}

	/// <summary>
	/// Verifies that generated sidebar dependency properties preserve their defaults and callbacks.
	/// </summary>
	[UITestMethod]
	public void PreservesGeneratedSidebarDependencyPropertyBehavior()
	{
		var item = new SidebarItem { NestingLevel = 2 };
		var view = new SidebarView { OpenPaneLength = 320d };

		Assert.IsNotNull(SidebarItem.MenuItemsSourceProperty);
		Assert.IsNotNull(SidebarView.MenuItemsSourceProperty);
		Assert.IsTrue(item.IsExpanded);
		Assert.IsTrue(item.SelectsOnInvoked);
		Assert.AreEqual(32d, item.IndentWidth);
		Assert.AreEqual(SidebarDisplayMode.Expanded, view.DisplayMode);
		Assert.IsTrue(view.CanResizePane);
		Assert.AreEqual(-320d, view.NegativeOpenPaneLength);
		Assert.AreEqual(TimeSpan.Zero, view.HoverToOpenDelay);
		Assert.AreEqual(TimeSpan.Zero, view.HoverToExpandDelay);
	}

	/// <summary>
	/// Verifies that recycled sidebar rows receive independent icon elements.
	/// </summary>
	[UITestMethod]
	public void CreatesDistinctSidebarIconElements()
	{
		var item = NavigationItemViewModel.CreateHome("Home");
		var firstIcon = item.Icon;
		var secondIcon = item.Icon;

		Assert.IsNotNull(firstIcon);
		Assert.IsNotNull(secondIcon);
		Assert.AreNotSame(firstIcon, secondIcon);
		Assert.IsInstanceOfType<ImageIcon>(firstIcon);
		StringAssert.Contains(((BitmapImage)((ImageIcon)firstIcon).Source).UriSource.AbsoluteUri, "/SidebarSections/Home");
	}

	/// <summary>
	/// Verifies the folder placeholder glyph fits the sidebar icon slot.
	/// </summary>
	[UITestMethod]
	public void CreatesFourteenPixelFolderPlaceholder()
	{
		var item = NavigationItemViewModel.CreateFolder("Folder", new StorableReference(new StorageSourceId("test"), "folder"));
		var iconElement = item.Icon;
		Assert.IsInstanceOfType<FontIcon>(iconElement);
		var icon = (FontIcon)iconElement!;

		Assert.AreEqual("\uE8B7", icon.Glyph);
		Assert.AreEqual(14d, icon.FontSize);
	}
}

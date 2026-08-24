// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Files.Controls
{
	[ContentProperty(Name = "InnerContent")]
	public sealed partial class SidebarView : UserControl
	{
		private const double CompactMaxWidth = 200;

		internal SidebarItem? SelectedItemContainer;

		private bool _draggingSidebarResizer;

		private double _preManipulationSidebarWidth;

		public event EventHandler<ItemInvokedEventArgs>? ItemInvoked;

		public event EventHandler<ItemContextInvokedArgs>? ItemContextInvoked;

		public event EventHandler<ItemDragOverEventArgs>? ItemDragOver;

		public event EventHandler<ItemDroppedEventArgs>? ItemDropped;

		public double VerticalScrollOffset => MenuItemHostScrollViewer?.VerticalOffset ?? 0;

		public SidebarView()
		{
			InitializeComponent();
			UpdateItemsSources();
			UpdateItemTemplates();
		}

		public void ScrollToVerticalOffset(double offset)
		{
			MenuItemHostScrollViewer?.ChangeView(null, offset, null, true);
		}

		internal void UpdateSelectedItemContainer(SidebarItem container)
		{
			SelectedItemContainer = container;
		}

		internal void RaiseItemInvoked(SidebarItem item, PointerUpdateKind pointerUpdateKind)
		{
			if (item.Item is null || !item.SelectsOnInvoked)
			{
				return;
			}

			SelectedItem = item.Item;
			ItemInvoked?.Invoke(item, new(pointerUpdateKind));
		}

		internal void RaiseContextRequested(SidebarItem item, Point e)
		{
			ItemContextInvoked?.Invoke(item, new(item.Item, e));
		}

		internal void RaiseItemDropped(SidebarItem sideBarItem, SidebarItemDropPosition dropPosition, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDropped?.Invoke(this, new(sideBarItem.Item, rawEvent.DataView, dropPosition, rawEvent));
		}

		internal void RaiseItemDragOver(SidebarItem sideBarItem, SidebarItemDropPosition dropPosition, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDragOver?.Invoke(this, new(sideBarItem.Item, rawEvent.DataView, dropPosition, rawEvent));
		}

		private void UpdateMinimalMode()
		{
			if (DisplayMode != SidebarDisplayMode.Minimal)
			{
				return;
			}

			if (IsPaneOpen)
			{
				VisualStateManager.GoToState(this, "MinimalExpanded", true);
			}
			else
			{
				VisualStateManager.GoToState(this, "MinimalCollapsed", true);
			}
		}

		private void UpdateDisplayMode()
		{
			switch (DisplayMode)
			{
				case SidebarDisplayMode.Compact:
					VisualStateManager.GoToState(this, "Compact", true);
					break;
				case SidebarDisplayMode.Expanded:
					UpdateOpenPaneLengthColumn();
					VisualStateManager.GoToState(this, "Expanded", true);
					break;
				case SidebarDisplayMode.Minimal:
					IsPaneOpen = false;
					UpdateMinimalMode();
					break;
			}

			UpdateRealizedMenuItemVisibility();
			UpdateResizerAvailability();
		}

		private void UpdateDisplayModeForPaneWidth(double newPaneWidth)
		{
			if (newPaneWidth < CompactMaxWidth)
			{
				DisplayMode = SidebarDisplayMode.Compact;
			}
			else if (newPaneWidth > CompactMaxWidth)
			{
				DisplayMode = SidebarDisplayMode.Expanded;
				OpenPaneLength = newPaneWidth;
			}
		}

		private void UpdateOpenPaneLengthColumn()
		{
			if (DisplayMode != SidebarDisplayMode.Expanded)
			{
				return;
			}

			PaneColumnDefinition.Width = new GridLength(OpenPaneLength);
		}

		private void UpdateResizerAvailability()
		{
			if (!CanResizePane)
			{
				SidebarResizer.Visibility = Visibility.Collapsed;
				SidebarResizer.IsHitTestVisible = false;

				return;
			}

			SidebarResizer.IsHitTestVisible = true;
			if (DisplayMode != SidebarDisplayMode.Minimal)
			{
				SidebarResizer.Visibility = Visibility.Visible;
			}
		}

		private void SidebarView_Loaded(object sender, RoutedEventArgs e)
		{
			UpdateDisplayMode();
			UpdateResizerAvailability();
			PaneColumnGrid.Translation = new System.Numerics.Vector3(0, 0, 32);
		}

		private void SidebarResizer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			if (!CanResizePane)
			{
				return;
			}

			_draggingSidebarResizer = true;
			_preManipulationSidebarWidth = PaneColumnGrid.ActualWidth;
			VisualStateManager.GoToState(this, "ResizerPressed", true);
			e.Handled = true;
		}

		private void SidebarResizer_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			var newWidth = _preManipulationSidebarWidth + e.Cumulative.Translation.X;
			UpdateDisplayModeForPaneWidth(newWidth);
			e.Handled = true;
		}

		private void SidebarResizerControl_KeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (!CanResizePane)
			{
				return;
			}

			if
			(e.Key != VirtualKey.Space && e.Key != VirtualKey.Enter && e.Key != VirtualKey.Left && e.Key != VirtualKey.Right && e.Key != VirtualKey.Control)
			{
				return;
			}

			var primaryInvocation = e.Key == VirtualKey.Space || e.Key == VirtualKey.Enter;
			if (DisplayMode == SidebarDisplayMode.Expanded)
			{
				if (primaryInvocation)
				{
					DisplayMode = SidebarDisplayMode.Compact;

					return;
				}

				var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
				var increment = ctrl.HasFlag(CoreVirtualKeyStates.Down) ? 5 : 1;

				// Left makes the pane smaller so we invert the increment
				if (e.Key == VirtualKey.Left)
				{
					increment = -increment;
				}

				var newWidth = OpenPaneLength + increment;
				UpdateDisplayModeForPaneWidth(newWidth);
				e.Handled = true;

				return;
			}
			else if (DisplayMode == SidebarDisplayMode.Compact)
			{
				if (primaryInvocation || e.Key == VirtualKey.Right)
				{
					DisplayMode = SidebarDisplayMode.Expanded;
					e.Handled = true;
				}
			}
		}

		private void PaneLightDismissLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			IsPaneOpen = false;
			e.Handled = true;
		}

		private void PaneLightDismissLayer_Tapped(object sender, TappedRoutedEventArgs e)
		{
			IsPaneOpen = false;
			e.Handled = true;
		}

		private void SidebarResizer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
		{
			if (!CanResizePane)
			{
				return;
			}

			if (DisplayMode == SidebarDisplayMode.Expanded)
			{
				DisplayMode = SidebarDisplayMode.Compact;
				e.Handled = true;
			}
			else
			{
				DisplayMode = SidebarDisplayMode.Expanded;
				e.Handled = true;
			}
		}

		private void SidebarResizer_PointerEntered(object sender, PointerRoutedEventArgs e)
		{
			if (!CanResizePane)
			{
				return;
			}

			var sidebarResizer = (FrameworkElement)sender;
			sidebarResizer.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast));
			VisualStateManager.GoToState(this, "ResizerPointerOver", true);
			e.Handled = true;
		}

		private void SidebarResizer_PointerExited(object sender, PointerRoutedEventArgs e)
		{
			if (!CanResizePane || _draggingSidebarResizer)
			{
				return;
			}

			var sidebarResizer = (FrameworkElement)sender;
			sidebarResizer.ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.Arrow));
			VisualStateManager.GoToState(this, "ResizerNormal", true);
			e.Handled = true;
		}

		private void SidebarResizer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			_draggingSidebarResizer = false;
			VisualStateManager.GoToState(this, "ResizerNormal", true);
			e.Handled = true;
		}

		private void MenuItemHostScrollViewer_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
		{
			ItemContextInvoked?.Invoke(this, new(null, e.TryGetPosition(this, out var point) ? point : default));
			e.Handled = true;
		}

		private void MenuItemsHost_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
		{
			PrepareSidebarItem(args.Element, true);
		}

		private void FooterMenuItemsHost_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
		{
			PrepareSidebarItem(args.Element, false);
		}

		private void PrepareSidebarItem(UIElement element, bool applyDepthVisibility)
		{
			if (element is SidebarItem sidebarItem)
			{
				// Recycled containers can retain an owner from another sidebar.
				sidebarItem.Owner = this;
				if (applyDepthVisibility)
				{
					UpdateMenuItemVisibility(sidebarItem);
				}

				sidebarItem.HandleItemChange();
			}
		}

		private void UpdateRealizedMenuItemVisibility()
		{
			for (int index = 0; ; index++)
			{
				if (MenuItemsHost.TryGetElement(index) is not UIElement element)
				{
					break;
				}

				if (element is SidebarItem sidebarItem)
				{
					UpdateMenuItemVisibility(sidebarItem);
				}
			}
		}

		private void UpdateMenuItemVisibility(SidebarItem item)
		{
			item.Visibility = DisplayMode is SidebarDisplayMode.Compact && item.DataContext is FlatSidebarItem { Depth: > 0 } ? Visibility.Collapsed : Visibility.Visible;
		}

		private void UpdateItemsSources()
		{
			if (MenuItemsHost is null || FooterMenuItemsHost is null)
			{
				return;
			}

			MenuItemsHost.ItemsSource = MenuItemsSource ?? MenuItems;
			FooterMenuItemsHost.ItemsSource = FooterMenuItemsSource ?? FooterMenuItems;
		}

		private void UpdateItemTemplates()
		{
			if (MenuItemsHost is null || FooterMenuItemsHost is null)
			{
				return;
			}

			var defaultTemplate = (DataTemplate)Resources["DefaultSidebarItemTemplate"];
			UpdateItemTemplate(MenuItemsHost, MenuItemTemplate, MenuItemTemplateSelector, defaultTemplate);
			UpdateItemTemplate(FooterMenuItemsHost, FooterMenuItemTemplate, FooterMenuItemTemplateSelector, defaultTemplate);
		}

		private static void UpdateItemTemplate(ItemsRepeater itemsHost, DataTemplate? itemTemplate, DataTemplateSelector? itemTemplateSelector, DataTemplate defaultTemplate)
		{
			if (itemTemplateSelector is not null)
			{
				itemsHost.ItemTemplate = itemTemplateSelector;

				return;
			}

			itemsHost.ItemTemplate = itemTemplate ?? defaultTemplate;
		}
	}
}

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
	[TemplatePart(Name = TemplatePartName_PaneColumnDefinition, Type = typeof(ColumnDefinition))]
	[TemplatePart(Name = TemplatePartName_PaneLightDismissLayer, Type = typeof(Grid))]
	[TemplatePart(Name = TemplatePartName_PaneColumnGrid, Type = typeof(Grid))]
	[TemplatePart(Name = TemplatePartName_MenuItemHostScrollViewer, Type = typeof(ScrollViewer))]
	[TemplatePart(Name = TemplatePartName_MenuItemsHost, Type = typeof(ItemsRepeater))]
	[TemplatePart(Name = TemplatePartName_FooterMenuItemsHost, Type = typeof(ItemsRepeater))]
	[TemplatePart(Name = TemplatePartName_SidebarResizer, Type = typeof(Border))]
	[TemplatePart(Name = TemplatePartName_SidebarResizerControl, Type = typeof(Control))]
	[ContentProperty(Name = "InnerContent")]
	public sealed partial class SidebarView : Control
	{
		private const double CompactMaxWidth = 200;
		private const string TemplatePartName_PaneColumnDefinition = "PaneColumnDefinition";
		private const string TemplatePartName_PaneLightDismissLayer = "PaneLightDismissLayer";
		private const string TemplatePartName_PaneColumnGrid = "PaneColumnGrid";
		private const string TemplatePartName_MenuItemHostScrollViewer = "MenuItemHostScrollViewer";
		private const string TemplatePartName_MenuItemsHost = "MenuItemsHost";
		private const string TemplatePartName_FooterMenuItemsHost = "FooterMenuItemsHost";
		private const string TemplatePartName_SidebarResizer = "SidebarResizer";
		private const string TemplatePartName_SidebarResizerControl = "SidebarResizerControl";

		internal SidebarItem? SelectedItemContainer;

		private ColumnDefinition? _paneColumnDefinition;
		private Grid? _paneLightDismissLayer;
		private Grid? _paneColumnGrid;
		private ScrollViewer? _menuItemHostScrollViewer;
		private ItemsRepeater? _menuItemsHost;
		private ItemsRepeater? _footerMenuItemsHost;
		private Border? _sidebarResizer;
		private Control? _sidebarResizerControl;
		private DataTemplate? _defaultSidebarItemTemplate;

		private bool _draggingSidebarResizer;

		private double _preManipulationSidebarWidth;

		public event EventHandler<ItemInvokedEventArgs>? ItemInvoked;

		public event EventHandler<ItemContextInvokedArgs>? ItemContextInvoked;

		public event EventHandler<ItemDragStartingEventArgs>? ItemDragStarting;

		public event EventHandler<ItemDragEnterEventArgs>? ItemDragEnter;

		public event EventHandler<ItemDragOverEventArgs>? ItemDragOver;

		public event EventHandler<ItemDragLeaveEventArgs>? ItemDragLeave;

		public event EventHandler<ItemDroppedEventArgs>? ItemDropped;

		public double VerticalScrollOffset => _menuItemHostScrollViewer?.VerticalOffset ?? 0;

		public SidebarView()
		{
			DefaultStyleKey = typeof(SidebarView);
			Loaded += SidebarView_Loaded;
		}

		public void ScrollToVerticalOffset(double offset)
		{
			_menuItemHostScrollViewer?.ChangeView(null, offset, null, true);
		}

		protected override void OnApplyTemplate()
		{
			UnhookTemplateParts();
			base.OnApplyTemplate();

			_paneColumnDefinition = GetRequiredTemplatePart<ColumnDefinition>(TemplatePartName_PaneColumnDefinition);
			_paneLightDismissLayer = GetRequiredTemplatePart<Grid>(TemplatePartName_PaneLightDismissLayer);
			_paneColumnGrid = GetRequiredTemplatePart<Grid>(TemplatePartName_PaneColumnGrid);
			_menuItemHostScrollViewer = GetRequiredTemplatePart<ScrollViewer>(TemplatePartName_MenuItemHostScrollViewer);
			_menuItemsHost = GetRequiredTemplatePart<ItemsRepeater>(TemplatePartName_MenuItemsHost);
			_footerMenuItemsHost = GetRequiredTemplatePart<ItemsRepeater>(TemplatePartName_FooterMenuItemsHost);
			_sidebarResizer = GetRequiredTemplatePart<Border>(TemplatePartName_SidebarResizer);
			_sidebarResizerControl = GetRequiredTemplatePart<Control>(TemplatePartName_SidebarResizerControl);
			_defaultSidebarItemTemplate = _menuItemsHost.ItemTemplate as DataTemplate;

			_paneLightDismissLayer.PointerPressed += PaneLightDismissLayer_PointerPressed;
			_paneLightDismissLayer.Tapped += PaneLightDismissLayer_Tapped;
			_menuItemHostScrollViewer.ContextRequested += MenuItemHostScrollViewer_ContextRequested;
			_menuItemsHost.ElementPrepared += MenuItemsHost_ElementPrepared;
			_footerMenuItemsHost.ElementPrepared += FooterMenuItemsHost_ElementPrepared;
			_sidebarResizer.DoubleTapped += SidebarResizer_DoubleTapped;
			_sidebarResizer.ManipulationCompleted += SidebarResizer_ManipulationCompleted;
			_sidebarResizer.ManipulationDelta += SidebarResizer_ManipulationDelta;
			_sidebarResizer.ManipulationStarted += SidebarResizer_ManipulationStarted;
			_sidebarResizer.PointerCanceled += SidebarResizer_PointerExited;
			_sidebarResizer.PointerEntered += SidebarResizer_PointerEntered;
			_sidebarResizer.PointerExited += SidebarResizer_PointerExited;
			_sidebarResizerControl.KeyDown += SidebarResizerControl_KeyDown;

			UpdateItemsSources();
			UpdateItemTemplates();
			GoToState(DisplayMode == SidebarDisplayMode.Expanded ? "Compact" : "Expanded", false);
			UpdateDisplayMode(false);
			UpdateResizerAvailability();
			_paneColumnGrid.Translation = new System.Numerics.Vector3(0, 0, 32);
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

		internal void RaiseItemDragStarting(SidebarItem sideBarItem, DragStartingEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDragStarting?.Invoke(this, new(sideBarItem.Item, rawEvent));
		}

		internal void RaiseItemDropped(SidebarItem sideBarItem, SidebarItemDropPosition dropPosition, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDropped?.Invoke(this, new(sideBarItem.Item, rawEvent.DataView, dropPosition, rawEvent));
		}

		internal void RaiseItemDragEnter(SidebarItem sideBarItem, SidebarItemDropPosition dropPosition, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDragEnter?.Invoke(this, new(sideBarItem.Item, rawEvent.DataView, dropPosition, rawEvent));
		}

		internal void RaiseItemDragOver(SidebarItem sideBarItem, SidebarItemDropPosition dropPosition, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDragOver?.Invoke(this, new(sideBarItem.Item, rawEvent.DataView, dropPosition, rawEvent));
		}

		internal void RaiseItemDragLeave(SidebarItem sideBarItem, DragEventArgs rawEvent)
		{
			if (sideBarItem.Item is null)
			{
				return;
			}

			ItemDragLeave?.Invoke(this, new(sideBarItem.Item, rawEvent));
		}

		private void UpdateMinimalMode(bool useTransitions = true)
		{
			if (DisplayMode != SidebarDisplayMode.Minimal)
			{
				return;
			}

			if (IsPaneOpen)
			{
				GoToState("MinimalExpanded", useTransitions);
			}
			else
			{
				GoToState("MinimalCollapsed", useTransitions);
			}
			UpdateOpenPaneLengthColumn();
		}

		private void UpdateDisplayMode(bool useTransitions = true)
		{
			switch (DisplayMode)
			{
				case SidebarDisplayMode.Compact:
					GoToState("Compact", useTransitions);
					break;
				case SidebarDisplayMode.Expanded:
					GoToState("Expanded", useTransitions);
					UpdateOpenPaneLengthColumn();
					break;
				case SidebarDisplayMode.Minimal:
					IsPaneOpen = false;
					UpdateMinimalMode(useTransitions);
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
			if (DisplayMode == SidebarDisplayMode.Compact || _paneColumnDefinition is null)
			{
				return;
			}

			_paneColumnDefinition.Width = new GridLength(OpenPaneLength);
		}

		private void UpdateResizerAvailability()
		{
			if (_sidebarResizer is null)
			{
				return;
			}

			if (!CanResizePane)
			{
				_sidebarResizer.Visibility = Visibility.Collapsed;
				_sidebarResizer.IsHitTestVisible = false;

				return;
			}

			_sidebarResizer.IsHitTestVisible = true;
			if (DisplayMode != SidebarDisplayMode.Minimal)
			{
				_sidebarResizer.Visibility = Visibility.Visible;
			}
		}

		private void SidebarView_Loaded(object sender, RoutedEventArgs e)
		{
			UpdateDisplayMode();
			UpdateResizerAvailability();
			if (_paneColumnGrid is not null)
			{
				_paneColumnGrid.Translation = new System.Numerics.Vector3(0, 0, 32);
			}
		}

		private void SidebarResizer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			if (!CanResizePane)
			{
				return;
			}

			_draggingSidebarResizer = true;
			_preManipulationSidebarWidth = _paneColumnGrid?.ActualWidth ?? OpenPaneLength;
			GoToState("ResizerPressed", true);
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
			GoToState("ResizerPointerOver", true);
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
			GoToState("ResizerNormal", true);
			e.Handled = true;
		}

		private void SidebarResizer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			_draggingSidebarResizer = false;
			GoToState("ResizerNormal", true);
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
				if (_menuItemsHost?.TryGetElement(index) is not UIElement element)
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
			if (_menuItemsHost is null || _footerMenuItemsHost is null)
			{
				return;
			}

			_menuItemsHost.ItemsSource = MenuItemsSource ?? MenuItems;
			_footerMenuItemsHost.ItemsSource = FooterMenuItemsSource ?? FooterMenuItems;
		}

		private void UpdateItemTemplates()
		{
			if (_menuItemsHost is null || _footerMenuItemsHost is null)
			{
				return;
			}

			UpdateItemTemplate(_menuItemsHost, MenuItemTemplate, MenuItemTemplateSelector, _defaultSidebarItemTemplate);
			UpdateItemTemplate(_footerMenuItemsHost, FooterMenuItemTemplate, FooterMenuItemTemplateSelector, _defaultSidebarItemTemplate);
		}

		private void UnhookTemplateParts()
		{
			if (_paneLightDismissLayer is not null)
			{
				_paneLightDismissLayer.PointerPressed -= PaneLightDismissLayer_PointerPressed;
				_paneLightDismissLayer.Tapped -= PaneLightDismissLayer_Tapped;
			}
			if (_menuItemHostScrollViewer is not null)
			{
				_menuItemHostScrollViewer.ContextRequested -= MenuItemHostScrollViewer_ContextRequested;
			}
			if (_menuItemsHost is not null)
			{
				_menuItemsHost.ElementPrepared -= MenuItemsHost_ElementPrepared;
			}
			if (_footerMenuItemsHost is not null)
			{
				_footerMenuItemsHost.ElementPrepared -= FooterMenuItemsHost_ElementPrepared;
			}
			if (_sidebarResizer is not null)
			{
				_sidebarResizer.DoubleTapped -= SidebarResizer_DoubleTapped;
				_sidebarResizer.ManipulationCompleted -= SidebarResizer_ManipulationCompleted;
				_sidebarResizer.ManipulationDelta -= SidebarResizer_ManipulationDelta;
				_sidebarResizer.ManipulationStarted -= SidebarResizer_ManipulationStarted;
				_sidebarResizer.PointerCanceled -= SidebarResizer_PointerExited;
				_sidebarResizer.PointerEntered -= SidebarResizer_PointerEntered;
				_sidebarResizer.PointerExited -= SidebarResizer_PointerExited;
			}
			if (_sidebarResizerControl is not null)
			{
				_sidebarResizerControl.KeyDown -= SidebarResizerControl_KeyDown;
			}
		}

		private T GetRequiredTemplatePart<T>(string name) where T : DependencyObject
		{
			if (GetTemplateChild(name) is T part)
			{
				return part;
			}

			throw new MissingFieldException($"Could not find {name} in the {nameof(SidebarView)} template.");
		}

		private bool GoToState(string stateName, bool useTransitions)
		{
			return VisualStateManager.GoToState(this, stateName, useTransitions);
		}

		private static void UpdateItemTemplate(ItemsRepeater itemsHost, DataTemplate? itemTemplate, DataTemplateSelector? itemTemplateSelector, DataTemplate? defaultTemplate)
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

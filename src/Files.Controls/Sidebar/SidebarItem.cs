// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using System.Collections.Specialized;

namespace Files.Controls
{
	public sealed partial class SidebarItem : Control
	{
		private const double DropRepositionThreshold = 0.2;

		private bool _isPointerOver;

		private bool _isClicking;

		private object? _selectedChildItem;

		private INotifyCollectionChanged? _childrenSource;

		private object? _defaultFlyoutItemTemplate;

		// Owner callbacks and template-child handlers have separate lifetimes because a recycled row can load before its template is applied.
		private bool _isWiredUp;

		private bool _isTemplateWired;

		private DispatcherQueueTimer? _dragOverTimer;

		private DispatcherQueueTimer? _dragOverExpandTimer;

		public bool HasChildren => (MenuItemsSource is IList enumerable && enumerable.Count > 0) || HasUnrealizedChildren;

		public bool IsGroupHeader => MenuItemsSource is not null;

		public bool CollapseEnabled => DisplayMode != SidebarDisplayMode.Compact;

		private bool HasChildSelection => _selectedChildItem is not null;

		public SidebarItem()
		{
			DefaultStyleKey = typeof(SidebarItem);

			PointerReleased += Item_PointerReleased;
			KeyDown += (sender, args) =>
			{
				switch (args.Key)
				{
					case Windows.System.VirtualKey.Enter:
						Clicked(PointerUpdateKind.Other);
						args.Handled = true;
						break;
					case Windows.System.VirtualKey.Right when HasChildren && CollapseEnabled && !IsExpanded:
						IsExpanded = true;
						args.Handled = true;
						break;
					case Windows.System.VirtualKey.Left when HasChildren && CollapseEnabled && IsExpanded:
						IsExpanded = false;
						args.Handled = true;
						break;
				}
			};
			DragStarting += SidebarItem_DragStarting;

			Loaded += SidebarItem_Loaded;
		}

		public void HandleItemChange()
		{
			UpdateFlyoutChildrenSource();
			UpdateExpansionState();
			ReevaluateSelection();
		}

		protected override AutomationPeer OnCreateAutomationPeer()
		{
			return new SidebarItemAutomationPeer(this);
		}

		// Template-tied work runs here because Loaded can fire before template parts are available.
		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			if (!_isTemplateWired)
			{
				_isTemplateWired = true;
				if (GetTemplateChild("ElementBorder") is Border border)
				{
					border.PointerEntered += ItemBorder_PointerEntered;
					border.PointerExited += ItemBorder_PointerExited;
					border.PointerCanceled += ItemBorder_PointerCanceled;
					border.PointerPressed += ItemBorder_PointerPressed;
					border.ContextRequested += ItemBorder_ContextRequested;
					border.DoubleTapped += ItemBorder_DoubleTapped;
					border.DragEnter += ItemBorder_DragEnter;
					border.DragLeave += ItemBorder_DragLeave;
					border.DragOver += ItemBorder_DragOver;
					border.Drop += ItemBorder_Drop;
					border.AllowDrop = true;
					border.IsTabStop = false;
				}
				if (GetTemplateChild("ChevronContainer") is Border chevronContainer)
				{
					chevronContainer.PointerPressed += ChevronContainer_PointerPressed;
				}

				if (GetTemplateChild("FlyoutChildrenPresenter") is ItemsRepeater flyoutRepeater)
				{
					flyoutRepeater.ElementPrepared += FlyoutChildrenPresenter_ElementPrepared;
					_defaultFlyoutItemTemplate ??= flyoutRepeater.ItemTemplate;
					flyoutRepeater.ItemsSource = MenuItemsSource;
					UpdateFlyoutItemTemplate();
				}
			}

			if (Owner is null)
			{
				return;
			}

			VisualStateManager.GoToState(this, Owner.SupportsExpansion ? "OwnerSupportsExpansion" : "OwnerDoesNotSupportExpansion", false);
			// Flyout items stay full-size even when their owner is compact.
			if (!IsInFlyout)
			{
				VisualStateManager.GoToState(this, DisplayMode == SidebarDisplayMode.Compact ? "Compact" : "NonCompact", false);
			}

			UpdateExpansionState();
		}

		internal void Select()
		{
			if (Owner is not null && Item is not null && SelectsOnInvoked)
			{
				Owner.SelectedItem = Item;
			}
		}

		// Allows SidebarView to refresh every realized row after selection changes.
		internal void ReevaluateSelectionFromOwner() => ReevaluateSelection();

		internal void Clicked(PointerUpdateKind pointerUpdateKind)
		{
			// Group headers toggle on row clicks; navigable tree rows toggle only from the chevron.
			if (IsGroupHeader && !SelectsOnInvoked)
			{
				if (CollapseEnabled)
				{
					IsExpanded = !IsExpanded;
				}
				else if (HasChildren)
				{
					SetFlyoutOpen(true);
				}
			}
			RaiseItemInvoked(pointerUpdateKind);
		}

		internal void RaiseItemInvoked(PointerUpdateKind pointerUpdateKind)
		{
			Owner?.RaiseItemInvoked(this, pointerUpdateKind);
		}

		private void SidebarItem_Loaded(object sender, RoutedEventArgs e)
		{
			// Loaded fires every time ItemsRepeater recycles the container; only the per-row HandleItemChange runs each time.
			if (!_isWiredUp)
			{
				HookupOwners();
				// Leave the row unwired when it has not been parented yet so the next Loaded event retries.
				if (Owner is not null)
				{
					_isWiredUp = true;
				}
			}
			HandleItemChange();
		}

		private void UpdateFlyoutChildrenSource()
		{
			if (GetTemplateChild("FlyoutChildrenPresenter") is ItemsRepeater flyoutRepeater)
			{
				flyoutRepeater.ItemsSource = MenuItemsSource;
			}
		}

		private void HookupOwners()
		{
			// Static rows are not prepared by an ItemsRepeater and must locate their owner in the visual tree.
			if (Owner is null)
			{
				Owner = this.FindAscendant<SidebarView>();
			}

			if (Owner is null)
			{
				return;
			}

			Owner.RegisterPropertyChangedCallback(SidebarView.DisplayModeProperty, (sender, args) => { DisplayMode = Owner.DisplayMode; });
			DisplayMode = Owner.DisplayMode;
			// Force the initial visual state because assigning the default display mode does not invoke the callback.
			if (!IsInFlyout)
			{
				VisualStateManager.GoToState(this, DisplayMode == SidebarDisplayMode.Compact ? "Compact" : "NonCompact", false);
			}

			// Static rows outside the repeaters still need selection updates.
			Owner.RegisterPropertyChangedCallback(SidebarView.SelectedItemProperty, (sender, args) => { ReevaluateSelection(); });
		}

		private void HandleMenuItemsSourceChange(object? newValue)
		{
			if (_childrenSource is not null)
			{
				_childrenSource.CollectionChanged -= ChildItems_CollectionChanged;
			}

			_childrenSource = newValue as INotifyCollectionChanged;
			if (_childrenSource is not null)
			{
				_childrenSource.CollectionChanged += ChildItems_CollectionChanged;
			}

			UpdateFlyoutChildrenSource();
			UpdateExpansionState();
			ReevaluateSelection();
		}

		private void UpdateFlyoutItemTemplate()
		{
			if (GetTemplateChild("FlyoutChildrenPresenter") is not ItemsRepeater flyoutRepeater)
			{
				return;
			}

			if (MenuItemTemplateSelector is not null)
			{
				flyoutRepeater.ItemTemplate = MenuItemTemplateSelector;

				return;
			}

			flyoutRepeater.ItemTemplate = MenuItemTemplate ?? _defaultFlyoutItemTemplate;
		}

		private void SidebarItem_DragStarting(UIElement sender, DragStartingEventArgs args)
		{
			Owner?.RaiseItemDragStarting(this, args);
		}

		private void SetFlyoutOpen(bool isOpen = true)
		{
			if (MenuItemsSource is null)
			{
				return;
			}

			var flyoutOwner = (GetTemplateChild("ElementGrid") as FrameworkElement)!;
			try
			{
				if (isOpen)
				{
					FlyoutBase.ShowAttachedFlyout(flyoutOwner);
				}
				else
				{
					FlyoutBase.GetAttachedFlyout(flyoutOwner).Hide();
				}
			}
			// The attached flyout is unavailable until the template is applied.
			catch (ArgumentException) { }
		}

		private void ChildItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			ReevaluateSelection();
			UpdateExpansionState();
			if (DisplayMode == SidebarDisplayMode.Compact && !HasChildren)
			{
				SetFlyoutOpen(false);
			}
		}

		private void ReevaluateSelection()
		{
			var selected = Owner?.SelectedItem;
			if (SelectsOnInvoked)
			{
				// Item-null guard avoids the null==null match that paints cleared/recycled containers as selected when SelectedItem is also null (e.g. after collapsing the section that held the active path).
				IsSelected = Item is not null && Item == selected;
				if (IsSelected)
				{
					Owner?.UpdateSelectedItemContainer(this);
				}
			}
			else
			{
				// Clear selection state retained by a recycled leaf container.
				IsSelected = false;
			}
			if (IsGroupHeader && MenuItemsSource is IList list && selected is not null && list.Contains(selected))
			{
				_selectedChildItem = selected;
				SetFlyoutOpen(false);
			}
			else
			{
				_selectedChildItem = null;
			}
			UpdateSelectionState();
		}

		// Flyout items live outside the flat list and need their selection state mirrored here so the realized row matches what the inline row would render.
		private void FlyoutChildrenPresenter_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
		{
			if (args.Element is SidebarItem item && MenuItemsSource is IList enumerable)
			{
				// Popup content cannot reliably locate its SidebarView through the visual tree.
				item.Owner = Owner;
				item.MenuItemTemplate ??= MenuItemTemplate;
				item.MenuItemTemplateSelector ??= MenuItemTemplateSelector;
				var newElement = enumerable[args.Index];
				item.IsSelected = newElement == _selectedChildItem;
				item.HandleItemChange();
			}
		}

		// Chevron press: suppress the bubbling press; otherwise ElementBorder treats the chevron click as a row click and raises ItemInvoked.
		private void ChevronContainer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
			=> e.Handled = TryToggleExpansion();

		private void ItemBorder_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
		{
			// Group headers already toggle on PointerReleased, while navigable tree rows reserve row double-click for expansion.
			if (IsGroupHeader && !SelectsOnInvoked)
			{
				e.Handled = true;

				return;
			}
			e.Handled = TryToggleExpansion();
		}

		private bool TryToggleExpansion()
		{
			if (!HasChildren || !CollapseEnabled)
			{
				return false;
			}

			IsExpanded = !IsExpanded;

			return true;
		}

		private void SidebarDisplayModeChanged()
		{
			switch (DisplayMode)
			{
				case SidebarDisplayMode.Expanded:
					UpdateExpansionState();
					UpdateSelectionState();
					SetFlyoutOpen(false);
					break;
				case SidebarDisplayMode.Minimal:
					UpdateExpansionState();
					SetFlyoutOpen(false);
					break;
				case SidebarDisplayMode.Compact:
					UpdateExpansionState();
					UpdateSelectionState();
					break;
			}
			if (!IsInFlyout)
			{
				VisualStateManager.GoToState(this, DisplayMode == SidebarDisplayMode.Compact ? "Compact" : "NonCompact", false);
				ReapplyOwnerExpansionState();
			}
		}

		private void ReapplyOwnerExpansionState()
		{
			if (Owner is null || Owner.SupportsExpansion)
			{
				return;
			}

			VisualStateManager.GoToState(this, "OwnerSupportsExpansion", false);
			VisualStateManager.GoToState(this, "OwnerDoesNotSupportExpansion", false);
		}

		private void UpdateSelectionState()
		{
			// Containers re-bind constantly during fast scroll; play state changes without transitions so no implicit animations fire on each ItemsRepeater realization.
			VisualStateManager.GoToState(this, ShouldShowSelectionIndicator() ? "Selected" : "Unselected", false);
			UpdatePointerState();
		}

		private bool ShouldShowSelectionIndicator()
		{
			if (IsExpanded && CollapseEnabled)
			{
				return IsSelected;
			}
			else
			{
				return IsSelected || HasChildSelection;
			}
		}

		private void UpdatePointerState(bool isPointerDown = false)
		{
			var useSelectedState = ShouldShowSelectionIndicator();
			if (isPointerDown)
			{
				VisualStateManager.GoToState(this, useSelectedState ? "PressedSelected" : "Pressed", false);
			}
			else if (_isPointerOver)
			{
				VisualStateManager.GoToState(this, useSelectedState ? "PointerOverSelected" : "PointerOver", false);
			}
			else
			{
				VisualStateManager.GoToState(this, useSelectedState ? "NormalSelected" : "Normal", false);
			}
		}

		private void UpdateExpansionState()
		{
			if (Owner?.SupportsExpansion == false)
			{
				VisualStateManager.GoToState(this, "NoExpansion", false);
				UpdateSelectionState();

				return;
			}

			if (MenuItemsSource is null || !CollapseEnabled)
			{
				VisualStateManager.GoToState(this, "NoExpansion", false);
			}
			else if (!HasChildren)
			{
				// Empty folder leaves render like normal leaves; empty group headers keep the section-heading style.
				VisualStateManager.GoToState(this, SelectsOnInvoked ? "NoExpansion" : "NoChildren", false);
			}
			else
			{
				VisualStateManager.GoToState(this, SelectsOnInvoked ? "LeafWithChildren" : (IsExpanded ? "Expanded" : "Collapsed"), false);
				VisualStateManager.GoToState(this, IsExpanded ? "ExpandedIconNormal" : "CollapsedIconNormal", false);
			}
			UpdateSelectionState();
		}

		private void ItemBorder_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
		{
			_isPointerOver = true;
			UpdatePointerState();
		}

		private void ItemBorder_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
		{
			_isPointerOver = false;
			_isClicking = false;
			UpdatePointerState();
		}

		private void ItemBorder_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
		{
			_isClicking = false;
			UpdatePointerState();
		}

		private void ItemBorder_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
		{
			_isClicking = true;
			UpdatePointerState(true);
			VisualStateManager.GoToState(this, IsExpanded ? "ExpandedIconPressed" : "CollapsedIconPressed", true);
		}

		private void Item_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
		{
			if (!_isClicking)
			{
				return;
			}

			_isClicking = false;
			e.Handled = true;
			UpdatePointerState();

			VisualStateManager.GoToState(this, IsExpanded ? "ExpandedIconNormal" : "CollapsedIconNormal", true);
			var pointerUpdateKind = e.GetCurrentPoint(null).Properties.PointerUpdateKind;
			if (pointerUpdateKind == PointerUpdateKind.LeftButtonReleased || pointerUpdateKind == PointerUpdateKind.MiddleButtonReleased)
			{
				Clicked(pointerUpdateKind);
			}
		}

		private void ItemBorder_DragEnter(object sender, DragEventArgs e)
		{
			Owner?.RaiseItemDragEnter(this, DetermineDropTargetPosition(e), e);
		}

		private async void ItemBorder_DragOver(object sender, DragEventArgs e)
		{
			var insertsAbove = DetermineDropTargetPosition(e);
			if (insertsAbove == SidebarItemDropPosition.Center)
			{
				VisualStateManager.GoToState(this, "DragOnTop", true);
			}
			else if (insertsAbove == SidebarItemDropPosition.Top)
			{
				VisualStateManager.GoToState(this, "DragInsertAbove", true);
			}
			else if (insertsAbove == SidebarItemDropPosition.Bottom)
			{
				VisualStateManager.GoToState(this, "DragInsertBelow", true);
			}

			Owner?.RaiseItemDragOver(this, insertsAbove, e);

			var openDelay = Owner?.HoverToOpenDelay ?? TimeSpan.Zero;
			var expandDelay = Owner?.HoverToExpandDelay ?? TimeSpan.Zero;
			var isCenter = insertsAbove == SidebarItemDropPosition.Center;
			var canHoverOpen = openDelay > TimeSpan.Zero && isCenter && Item is not null && SelectsOnInvoked;
			var canHoverExpand = expandDelay > TimeSpan.Zero && isCenter && HasChildren && CollapseEnabled;
			if (canHoverExpand)
			{
				_dragOverExpandTimer ??= DispatcherQueue.CreateTimer();
				_dragOverExpandTimer.Debounce(() => { _dragOverExpandTimer.Stop(); IsExpanded = true; }, expandDelay, false);
			}
			else
			{
				_dragOverExpandTimer?.Stop();
			}
			if (canHoverOpen)
			{
				_dragOverTimer ??= DispatcherQueue.CreateTimer();
				_dragOverTimer.Debounce(() => { _dragOverTimer.Stop(); RaiseItemInvoked(PointerUpdateKind.Other); }, openDelay, false);
			}
			else
			{
				_dragOverTimer?.Stop();
			}
		}

		private void ItemBorder_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
		{
			Owner?.RaiseContextRequested(this, args.TryGetPosition(this, out var point) ? point : default);
			args.Handled = true;
		}

		private void ItemBorder_DragLeave(object sender, DragEventArgs e)
		{
			_dragOverTimer?.Stop();
			_dragOverExpandTimer?.Stop();
			UpdatePointerState();
			Owner?.RaiseItemDragLeave(this, e);
		}

		private void ItemBorder_Drop(object sender, DragEventArgs e)
		{
			_dragOverTimer?.Stop();
			_dragOverExpandTimer?.Stop();
			UpdatePointerState();
			Owner?.RaiseItemDropped(this, DetermineDropTargetPosition(e), e);
		}

		private SidebarItemDropPosition DetermineDropTargetPosition(DragEventArgs args)
		{
			if (UseReorderDrop)
			{
				if (GetTemplateChild("ElementGrid") is Grid grid)
				{
					var position = args.GetPosition(grid);
					if (position.Y < grid.ActualHeight * DropRepositionThreshold)
					{
						return SidebarItemDropPosition.Top;
					}
					if (position.Y > grid.ActualHeight * (1 - DropRepositionThreshold))
					{
						return SidebarItemDropPosition.Bottom;
					}

					return SidebarItemDropPosition.Center;
				}
			}

			return SidebarItemDropPosition.Center;
		}
	}
}

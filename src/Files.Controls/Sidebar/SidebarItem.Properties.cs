// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;

namespace Files.Controls
{
	public sealed partial class SidebarItem : Control
	{
		[GeneratedDependencyProperty]
		public partial SidebarView? Owner { get; set; }

		[GeneratedDependencyProperty]
		public partial bool IsSelected { get; set; }

		[GeneratedDependencyProperty(DefaultValue = true)]
		public partial bool IsExpanded { get; set; }

		[GeneratedDependencyProperty]
		public partial int NestingLevel { get; set; }

		[GeneratedDependencyProperty(DefaultValue = 0d)]
		public partial double IndentWidth { get; set; }

		[GeneratedDependencyProperty(DefaultValue = 1d)]
		public partial double ContentOpacity { get; set; }

		[GeneratedDependencyProperty]
		public partial bool IsInFlyout { get; set; }

		[GeneratedDependencyProperty]
		public partial object? Item { get; set; }

		[GeneratedDependencyProperty]
		public partial object? MenuItemsSource { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplate? MenuItemTemplate { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplateSelector? MenuItemTemplateSelector { get; set; }

		[GeneratedDependencyProperty(DefaultValue = true)]
		public partial bool SelectsOnInvoked { get; set; }

		[GeneratedDependencyProperty]
		public partial bool HasUnrealizedChildren { get; set; }

		[GeneratedDependencyProperty]
		public partial string? DragPath { get; set; }

		[GeneratedDependencyProperty]
		public partial bool UseReorderDrop { get; set; }

		[GeneratedDependencyProperty]
		public partial FrameworkElement? Icon { get; set; }

		[GeneratedDependencyProperty]
		public partial FrameworkElement? Decorator { get; set; }

		[GeneratedDependencyProperty(DefaultValue = SidebarDisplayMode.Expanded)]
		public partial SidebarDisplayMode DisplayMode { get; set; }

		[GeneratedDependencyProperty]
		public partial string? Text { get; set; }

		[GeneratedDependencyProperty]
		public partial object? ToolTip { get; set; }

		partial void OnOwnerChanged(SidebarView? newValue)
		{
			if (newValue is null)
			{
				UnhookOwnerCallbacks();

				return;
			}

			HookupOwnerCallbacks(newValue);
			VisualStateManager.GoToState(this, newValue.SupportsExpansion ? "OwnerSupportsExpansion" : "OwnerDoesNotSupportExpansion", false);
		}

		partial void OnIsSelectedChanged(bool newValue)
		{
			UpdateSelectionState();
		}

		partial void OnIsExpandedChanged(bool newValue)
		{
			UpdateExpansionState();
		}

		partial void OnNestingLevelChanged(int newValue)
		{
			IndentWidth = newValue * 16d;
		}

		partial void OnItemChanged(object? newValue)
		{
			HandleItemChange();
		}

		partial void OnMenuItemsSourceChanged(object? newValue)
		{
			HandleMenuItemsSourceChange(newValue);
		}

		partial void OnMenuItemTemplateChanged(DataTemplate? newValue)
		{
			UpdateFlyoutItemTemplate();
		}

		partial void OnMenuItemTemplateSelectorChanged(DataTemplateSelector? newValue)
		{
			UpdateFlyoutItemTemplate();
		}

		partial void OnSelectsOnInvokedChanged(bool newValue)
		{
			UpdateExpansionState();
			ReevaluateSelectionFromOwner();
		}

		partial void OnHasUnrealizedChildrenChanged(bool newValue)
		{
			UpdateExpansionState();
			ReevaluateSelectionFromOwner();
		}

		partial void OnDragPathChanged(string? newValue)
		{
			UpdateCanDrag();
		}

		partial void OnDisplayModeChanged(SidebarDisplayMode newValue)
		{
			SidebarDisplayModeChanged();
		}
	}
}

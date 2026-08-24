// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;

namespace Files.Controls
{
	public sealed partial class SidebarView
	{
		[GeneratedDependencyProperty(DefaultValue = SidebarDisplayMode.Expanded)]
		public partial SidebarDisplayMode DisplayMode { get; set; }

		[GeneratedDependencyProperty]
		public partial UIElement? InnerContent { get; set; }

		[GeneratedDependencyProperty]
		public partial UIElement? SidebarContent { get; set; }

		[GeneratedDependencyProperty]
		public partial UIElement? Header { get; set; }

		[GeneratedDependencyProperty]
		public partial UIElement? Footer { get; set; }

		[GeneratedDependencyProperty]
		public partial Microsoft.UI.Xaml.Media.Brush? PaneBackgroundBrush { get; set; }

		[GeneratedDependencyProperty]
		public partial bool IsPaneOpen { get; set; }

		[GeneratedDependencyProperty(DefaultValue = 240d)]
		public partial double OpenPaneLength { get; set; }

		[GeneratedDependencyProperty(DefaultValue = -240d)]
		public partial double NegativeOpenPaneLength { get; set; }

		[GeneratedDependencyProperty(DefaultValue = true)]
		public partial bool CanResizePane { get; set; }

		[GeneratedDependencyProperty]
		public partial object? SelectedItem { get; set; }

		[GeneratedDependencyProperty]
		public partial object? MenuItemsSource { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplate? MenuItemTemplate { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplateSelector? MenuItemTemplateSelector { get; set; }

		[GeneratedDependencyProperty]
		public partial object? FooterMenuItemsSource { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplate? FooterMenuItemTemplate { get; set; }

		[GeneratedDependencyProperty]
		public partial DataTemplateSelector? FooterMenuItemTemplateSelector { get; set; }

		[GeneratedDependencyProperty]
		public partial TimeSpan HoverToOpenDelay { get; set; }

		[GeneratedDependencyProperty]
		public partial TimeSpan HoverToExpandDelay { get; set; }

		public IList<object> MenuItems { get; } = new ObservableCollection<object>();

		public IList<object> FooterMenuItems { get; } = new ObservableCollection<object>();

		// Off by default; flat-list sidebars (Settings) collapse the chevron column. Opt in for hierarchical sidebars (main tree view).
		public bool SupportsExpansion { get; set; }

		partial void OnDisplayModeChanged(SidebarDisplayMode newValue)
		{
			UpdateDisplayMode();
		}

		partial void OnIsPaneOpenChanged(bool newValue)
		{
			UpdateMinimalMode();
		}

		partial void OnOpenPaneLengthChanged(double newValue)
		{
			NegativeOpenPaneLength = -newValue;
			UpdateOpenPaneLengthColumn();
		}

		partial void OnCanResizePaneChanged(bool newValue)
		{
			UpdateResizerAvailability();
		}

		partial void OnSelectedItemChanged(object? newValue)
		{
			SelectedItemContainer = null;
			ReevaluateSelection(MenuItemsHost);
			ReevaluateSelection(FooterMenuItemsHost);
		}

		partial void OnMenuItemsSourceChanged(object? newValue)
		{
			UpdateItemsSources();
		}

		partial void OnFooterMenuItemsSourceChanged(object? newValue)
		{
			UpdateItemsSources();
		}

		partial void OnMenuItemTemplateChanged(DataTemplate? newValue)
		{
			UpdateItemTemplates();
		}

		partial void OnMenuItemTemplateSelectorChanged(DataTemplateSelector? newValue)
		{
			UpdateItemTemplates();
		}

		partial void OnFooterMenuItemTemplateChanged(DataTemplate? newValue)
		{
			UpdateItemTemplates();
		}

		partial void OnFooterMenuItemTemplateSelectorChanged(DataTemplateSelector? newValue)
		{
			UpdateItemTemplates();
		}

		private static void ReevaluateSelection(ItemsRepeater? itemsHost)
		{
			if (itemsHost is null)
			{
				return;
			}

			for (int i = 0; ; i++)
			{
				var element = itemsHost.TryGetElement(i);
				if (element is null)
				{
					break;
				}

				if (element is SidebarItem sidebarItem)
				{
					sidebarItem.ReevaluateSelectionFromOwner();
				}
			}
		}
	}
}

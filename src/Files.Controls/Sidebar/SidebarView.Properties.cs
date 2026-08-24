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

		partial void OnDisplayModePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateDisplayMode();
		}

		partial void OnIsPaneOpenPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateMinimalMode();
		}

		partial void OnOpenPaneLengthPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			NegativeOpenPaneLength = -(double)e.NewValue;
			UpdateOpenPaneLengthColumn();
		}

		partial void OnCanResizePanePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateResizerAvailability();
		}

		partial void OnSelectedItemPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			SelectedItemContainer = null;
			ReevaluateSelection(_menuItemsHost);
			ReevaluateSelection(_footerMenuItemsHost);
		}

		partial void OnMenuItemsSourcePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateItemsSources();
		}

		partial void OnFooterMenuItemsSourcePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateItemsSources();
		}

		partial void OnMenuItemTemplatePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateItemTemplates();
		}

		partial void OnMenuItemTemplateSelectorPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateItemTemplates();
		}

		partial void OnFooterMenuItemTemplatePropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			UpdateItemTemplates();
		}

		partial void OnFooterMenuItemTemplateSelectorPropertyChanged(DependencyPropertyChangedEventArgs e)
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

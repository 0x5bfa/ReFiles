// Copyright (c) Files Community
// Licensed under the MIT License.

using CommunityToolkit.WinUI;

namespace Files.Controls
{
	public sealed partial class SidebarItem : Control
	{
		public static readonly DependencyProperty OwnerProperty =
			DependencyProperty.Register(nameof(Owner), typeof(SidebarView), typeof(SidebarItem), new PropertyMetadata(null, OnOwnerChanged));

		public static readonly DependencyProperty IsSelectedProperty =
			DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(SidebarItem), new PropertyMetadata(false, OnPropertyChanged));

		public static readonly DependencyProperty IsExpandedProperty =
			DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(SidebarItem), new PropertyMetadata(true, OnPropertyChanged));

		public static readonly DependencyProperty NestingLevelProperty =
			DependencyProperty.Register(nameof(NestingLevel), typeof(int), typeof(SidebarItem), new PropertyMetadata(0, OnNestingLevelChanged));

		public static readonly DependencyProperty IndentWidthProperty =
			DependencyProperty.Register(nameof(IndentWidth), typeof(double), typeof(SidebarItem), new PropertyMetadata(0d));

		public static readonly DependencyProperty ContentOpacityProperty =
			DependencyProperty.Register(nameof(ContentOpacity), typeof(double), typeof(SidebarItem), new PropertyMetadata(1.0));

		public static readonly DependencyProperty IsInFlyoutProperty =
			DependencyProperty.Register(nameof(IsInFlyout), typeof(bool), typeof(SidebarItem), new PropertyMetadata(false));

		public static readonly DependencyProperty ItemProperty =
			DependencyProperty.Register(nameof(Item), typeof(ISidebarItemModel), typeof(SidebarItem), new PropertyMetadata(null));

		public static readonly DependencyProperty UseReorderDropProperty =
			DependencyProperty.Register(nameof(UseReorderDrop), typeof(bool), typeof(SidebarItem), new PropertyMetadata(false));

		public static readonly DependencyProperty IconProperty =
			DependencyProperty.Register(nameof(Icon), typeof(FrameworkElement), typeof(SidebarItem), new PropertyMetadata(null));

		public static readonly DependencyProperty DecoratorProperty =
			DependencyProperty.Register(nameof(Decorator), typeof(FrameworkElement), typeof(SidebarItem), new PropertyMetadata(null));

		public static readonly DependencyProperty DisplayModeProperty =
			DependencyProperty.Register(nameof(DisplayMode), typeof(SidebarDisplayMode), typeof(SidebarItem), new PropertyMetadata(SidebarDisplayMode.Expanded, OnPropertyChanged));

		public SidebarView? Owner
		{
			get { return (SidebarView?)GetValue(OwnerProperty); }
			set { SetValue(OwnerProperty, value); }
		}

		public bool IsSelected
		{
			get { return (bool)GetValue(IsSelectedProperty); }
			set { SetValue(IsSelectedProperty, value); }
		}

		public bool IsExpanded
		{
			get { return (bool)GetValue(IsExpandedProperty); }
			set { SetValue(IsExpandedProperty, value); }
		}

		public int NestingLevel
		{
			get { return (int)GetValue(NestingLevelProperty); }
			set { SetValue(NestingLevelProperty, value); }
		}

		public double IndentWidth
		{
			get { return (double)GetValue(IndentWidthProperty); }
			set { SetValue(IndentWidthProperty, value); }
		}

		// Dims icon + text + chevron + decorator only; the selection indicator and pointer-over fill stay at full opacity so a selected hidden row still reads as selected.
		public double ContentOpacity
		{
			get { return (double)GetValue(ContentOpacityProperty); }
			set { SetValue(ContentOpacityProperty, value); }
		}

		public bool IsInFlyout
		{
			get { return (bool)GetValue(IsInFlyoutProperty); }
			set { SetValue(IsInFlyoutProperty, value); }
		}

		public ISidebarItemModel? Item
		{
			get { return (ISidebarItemModel)GetValue(ItemProperty); }
			set { SetValue(ItemProperty, value); }
		}

		public bool UseReorderDrop
		{
			get { return (bool)GetValue(UseReorderDropProperty); }
			set { SetValue(UseReorderDropProperty, value); }
		}

		public FrameworkElement? Icon
		{
			get { return (FrameworkElement?)GetValue(IconProperty); }
			set { SetValue(IconProperty, value); }
		}

		public FrameworkElement? Decorator
		{
			get { return (FrameworkElement?)GetValue(DecoratorProperty); }
			set { SetValue(DecoratorProperty, value); }
		}

		public SidebarDisplayMode DisplayMode
		{
			get { return (SidebarDisplayMode)GetValue(DisplayModeProperty); }
			set { SetValue(DisplayModeProperty, value); }
		}

		[GeneratedDependencyProperty]
		public partial string? Text { get; set; }

		[GeneratedDependencyProperty]
		public partial object? ToolTip { get; set; }

		public static void OnPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
		{
			if (sender is not SidebarItem item)
			{
				return;
			}

			if (e.Property == DisplayModeProperty)
			{
				item.SidebarDisplayModeChanged((SidebarDisplayMode)e.OldValue);
			}
			else if (e.Property == IsSelectedProperty)
			{
				item.UpdateSelectionState();
			}
			else if (e.Property == IsExpandedProperty)
			{
				item.UpdateExpansionState();
			}
			else if (e.Property == ItemProperty)
			{
				item.HandleItemChange();
			}
			else
			{
				Debug.Write(e.Property.ToString());
			}
		}

		// Owner is assigned by the hosting ItemsRepeater's ElementPrepared (top-level rows) or by the parent row (flyout children) — recycled containers can carry a stale Owner across realizations, so the chevron-column visual state must re-apply whenever Owner flips.
		private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is SidebarItem item && item.Owner is { } owner)
			{
				VisualStateManager.GoToState(item, owner.SupportsExpansion ? "OwnerSupportsExpansion" : "OwnerDoesNotSupportExpansion", false);
			}
		}

		private static void OnNestingLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is SidebarItem item && e.NewValue is int level)
			{
				item.IndentWidth = level * 16d;
			}
		}
	}
}

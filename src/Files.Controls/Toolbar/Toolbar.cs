// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Files.Controls
{
	public partial class Toolbar : Control
	{
		private const double DefaultItemSpacing = 4d;
		private const double DefaultOverflowButtonWidth = 40d;

		private readonly List<MaterializedToolbarItem> _materializedItems = [];
		private readonly List<ToolbarItem> _subscribedItems = [];
		private readonly Dictionary<ToolbarItem, MaterializedToolbarItem> _itemOwners = [];
		private List<IToolbarItemSet> _visibleItems = [];
		private List<ToolbarItem> _overflowItems = [];

		private ItemsRepeater? _itemsRepeater;
		private StackPanel? _overflowStackPanel;
		private ToolbarButton? _overflowButton;
		private MenuFlyout? _overflowFlyout;
		private INotifyCollectionChanged? _itemsCollection;
		private bool _overflowMenuDirty = true;
		private bool _layoutUpdateQueued;
		private bool _templateApplied;

		private double _smallMinWidth = 32;
		private double _mediumMinWidth = 32;
		private double _largeMinWidth = 40;
		private double _smallMinHeight = 32;
		private double _mediumMinHeight = 40;
		private double _largeMinHeight = 40;
		private double _currentMinWidth = 32;
		private double _currentMinHeight = 40;

		/// <summary>
		/// Initializes a new toolbar.
		/// </summary>
		public Toolbar()
		{
			DefaultStyleKey = typeof(Toolbar);
			Items = [];
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
			SizeChanged += OnSizeChanged;
			UpdateMinSizesFromResources();
		}

		protected override void OnApplyTemplate()
		{
			if (_itemsRepeater is not null)
			{
				_itemsRepeater.ItemsSource = null;
			}

			_templateApplied = false;
			base.OnApplyTemplate();

			_itemsRepeater = GetTemplateChild(ToolbarItemsRepeaterPartName) as ItemsRepeater;
			_overflowStackPanel = GetTemplateChild(OverflowStackPanelPartName) as StackPanel;
			_overflowButton = GetTemplateChild(OverflowButtonPartName) as ToolbarButton;
			_overflowFlyout = GetTemplateChild(OverflowFlyoutPartName) as MenuFlyout ?? _overflowButton?.Flyout as MenuFlyout;

			if (_itemsRepeater is not null)
			{
				_itemsRepeater.ItemTemplate = ItemTemplate;
			}

			if (_overflowButton is not null)
			{
				_overflowButton.Label = OverflowButtonLabel;
			}

			_templateApplied = true;
			RebuildMaterializedItems();
			RequestLayoutUpdate();
		}

		private void OnLoaded(object sender, RoutedEventArgs args)
		{
			ItemsChanged(Items);
			RequestLayoutUpdate();
		}

		private void OnUnloaded(object sender, RoutedEventArgs args)
		{
			_layoutUpdateQueued = false;
			ClearItemSubscriptions();
			if (_itemsCollection is not null)
			{
				_itemsCollection.CollectionChanged -= OnItemsCollectionChanged;
				_itemsCollection = null;
			}
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs args)
		{
			RequestLayoutUpdate();
		}

		private void ItemsChanged(IList<ToolbarItem>? newItems)
		{
			if (ReferenceEquals(_itemsCollection, newItems))
			{
				return;
			}

			ClearItemSubscriptions();
			if (_itemsCollection is not null)
			{
				_itemsCollection.CollectionChanged -= OnItemsCollectionChanged;
			}

			_itemsCollection = newItems as INotifyCollectionChanged;
			if (_itemsCollection is not null)
			{
				_itemsCollection.CollectionChanged += OnItemsCollectionChanged;
			}

			RebuildMaterializedItems();
			RequestLayoutUpdate();
		}

		private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
		{
			RebuildMaterializedItems();
			RequestLayoutUpdate();
		}

		private void RebuildMaterializedItems()
		{
			ReleaseMaterializedItems();
			ClearItemSubscriptions();
			_materializedItems.Clear();
			_overflowMenuDirty = true;

			if (_templateApplied && _itemsRepeater is not null && Items is not null)
			{
				foreach (var item in Items)
				{
					if (item is null)
					{
						continue;
					}

					var materializedItem = new MaterializedToolbarItem(item, CreateToolbarItem(item));
					_materializedItems.Add(materializedItem);
					SubscribeToItemTree(item, materializedItem);
				}
			}

			_visibleItems = [];
			_overflowItems = [];
			if (_itemsRepeater is not null)
			{
				_itemsRepeater.ItemsSource = _visibleItems;
			}
		}

		private void ReleaseMaterializedItems()
		{
			foreach (var materializedItem in _materializedItems)
			{
				if (materializedItem.Element is ContentControl contentControl)
				{
					contentControl.Content = null;
				}
			}
		}

		private void SubscribeToItemTree(ToolbarItem item, MaterializedToolbarItem owner)
		{
			item.PropertyChanged += OnToolbarItemPropertyChanged;
			_subscribedItems.Add(item);
			_itemOwners[item] = owner;

			if (item.SubItems is null)
			{
				return;
			}

			foreach (var subItem in item.SubItems)
			{
				if (subItem is not null)
				{
					SubscribeToItemTree(subItem, owner);
				}
			}
		}

		private void ClearItemSubscriptions()
		{
			foreach (var item in _subscribedItems)
			{
				item.PropertyChanged -= OnToolbarItemPropertyChanged;
			}

			_subscribedItems.Clear();
			_itemOwners.Clear();
		}

		private void OnToolbarItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (sender is not ToolbarItem item)
			{
				return;
			}

			_overflowMenuDirty = true;
			if (args.PropertyName is nameof(ToolbarItem.ItemType) or nameof(ToolbarItem.SubItems))
			{
				RebuildMaterializedItems();
			}
			else
			{
				if (_itemOwners.TryGetValue(item, out var materializedItem))
				{
					ApplyToolbarItem(materializedItem.Element, materializedItem.Item);
				}
			}

			RequestLayoutUpdate();
		}

		private FrameworkElement CreateToolbarItem(ToolbarItem item)
		{
			FrameworkElement element = item.ItemType switch
			{
				ToolbarItemTypes.Button => new ToolbarButton(),
				ToolbarItemTypes.ToggleButton => new ToolbarToggleButton(),
				ToolbarItemTypes.FlyoutButton => new ToolbarFlyoutButton(),
				ToolbarItemTypes.RadioButton => new ToolbarRadioButton(),
				ToolbarItemTypes.SplitButton => new ToolbarSplitButton(),
				ToolbarItemTypes.Separator => new ToolbarSeparator(),
				_ => new ToolbarContentHost(),
			};

			ApplyToolbarItem(element, item);
			if (element is ToolbarToggleButton toggleButton)
			{
				toggleButton.Checked += (_, _) => item.IsChecked = true;
				toggleButton.Unchecked += (_, _) => item.IsChecked = false;
			}
			else if (element is ToolbarRadioButton radioButton)
			{
				radioButton.Checked += (_, _) => item.IsChecked = true;
			}

			return element;
		}

		private void ApplyToolbarItem(FrameworkElement element, ToolbarItem item)
		{
			SetCommonItemProperties(element, item);

			switch (element)
			{
				case ToolbarButton button:
					button.Label = item.Label;
					button.ThemedIcon = item.ThemedIcon;
					button.IconSize = item.IconSize;
					button.Content = item.Content;
					button.Command = item.Command;
					button.CommandParameter = item.CommandParameter;
					break;
				case ToolbarToggleButton toggleButton:
					toggleButton.Label = item.Label;
					toggleButton.ThemedIcon = item.ThemedIcon;
					toggleButton.IconSize = item.IconSize;
					toggleButton.Content = item.Content;
					toggleButton.Command = item.Command;
					toggleButton.CommandParameter = item.CommandParameter;
					toggleButton.IsChecked = item.IsChecked;
					break;
				case ToolbarFlyoutButton flyoutButton:
					flyoutButton.Content = CreateToolbarContent(item);
					flyoutButton.Flyout = item.Flyout ?? CreateFlyout(item.SubItems);
					flyoutButton.Command = item.Command;
					flyoutButton.CommandParameter = item.CommandParameter;
					break;
				case ToolbarRadioButton radioButton:
					radioButton.Content = CreateToolbarContent(item);
					radioButton.GroupName = item.GroupName;
					radioButton.IsChecked = item.IsChecked;
					radioButton.Command = item.Command;
					radioButton.CommandParameter = item.CommandParameter;
					break;
				case ToolbarSplitButton splitButton:
					splitButton.Content = CreateToolbarContent(item);
					splitButton.Flyout = item.Flyout ?? CreateFlyout(item.SubItems);
					splitButton.Command = item.Command;
					splitButton.CommandParameter = item.CommandParameter;
					break;
				case ToolbarContentHost contentHost:
					contentHost.Content = item.Content;
					break;
			}
		}

		private void SetCommonItemProperties(FrameworkElement element, ToolbarItem item)
		{
			element.MinWidth = _currentMinWidth;
			element.MinHeight = _currentMinHeight;
			element.HorizontalAlignment = HorizontalAlignment.Center;
			element.VerticalAlignment = VerticalAlignment.Center;
			ToolTipService.SetToolTip(element, string.IsNullOrWhiteSpace(item.Label) ? null : item.Label);

			if (element is Control control)
			{
				control.IsEnabled = item.IsEnabled;
				control.HorizontalContentAlignment = HorizontalAlignment.Center;
				control.VerticalContentAlignment = VerticalAlignment.Center;
				AutomationProperties.SetName(control, item.Label);
				control.KeyboardAccelerators.Clear();
				if (item.KeyboardAccelerators is not null)
				{
					foreach (var keyboardAccelerator in item.KeyboardAccelerators)
					{
						control.KeyboardAccelerators.Add(CloneKeyboardAccelerator(keyboardAccelerator));
					}
				}
			}

			if (element is ToolbarSeparator separator)
			{
				separator.MinWidth = 1;
			}
		}

		private static KeyboardAccelerator CloneKeyboardAccelerator(KeyboardAccelerator source)
		{
			ArgumentNullException.ThrowIfNull(source);

			return new KeyboardAccelerator
			{
				Key = source.Key,
				Modifiers = source.Modifiers,
			};
		}

		private static object? CreateToolbarContent(ToolbarItem item)
		{
			if (item.Content is not null)
			{
				return item.Content;
			}

			return CreateIcon(item);
		}

		private static ThemedIcon? CreateIcon(ToolbarItem item)
		{
			if (item.ThemedIcon is null)
			{
				return null;
			}

			return new ThemedIcon { Data = item.ThemedIcon, IconSize = item.IconSize };
		}

		private static MenuFlyout CreateFlyout(IList<ToolbarItem>? items)
		{
			var flyout = new MenuFlyout();
			if (items is not null)
			{
				AddMenuItems(items, flyout.Items);
			}

			return flyout;
		}

		private static void AddMenuItems(IEnumerable<ToolbarItem> items, IList<MenuFlyoutItemBase> destination)
		{
			foreach (var item in items)
			{
				if (item is not null)
				{
					destination.Add(CreateMenuItem(item));
				}
			}
		}

		private static MenuFlyoutItemBase CreateMenuItem(ToolbarItem item)
		{
			if (item.ItemType == ToolbarItemTypes.Separator)
			{
				return new MenuFlyoutSeparator();
			}

			if (item.ItemType == ToolbarItemTypes.RadioButton)
			{
				var radioItem = new RadioMenuFlyoutItem
				{
					Text = item.Label,
					GroupName = item.GroupName,
					IsChecked = item.IsChecked,
					Command = item.Command,
					CommandParameter = item.CommandParameter,
					KeyboardAcceleratorTextOverride = item.KeyboardAcceleratorTextOverride,
					Icon = CreateIcon(item),
				};
				radioItem.Click += (_, _) => item.IsChecked = true;

				return radioItem;
			}

			if (item.ItemType == ToolbarItemTypes.ToggleButton)
			{
				var toggleItem = new ToggleMenuFlyoutItem
				{
					Text = item.Label,
					IsChecked = item.IsChecked,
					Command = item.Command,
					CommandParameter = item.CommandParameter,
					KeyboardAcceleratorTextOverride = item.KeyboardAcceleratorTextOverride,
					Icon = CreateIcon(item),
				};
				toggleItem.Click += (_, _) => item.IsChecked = toggleItem.IsChecked;

				return toggleItem;
			}

			if (item.ItemType is ToolbarItemTypes.FlyoutButton or ToolbarItemTypes.SplitButton || item.SubItems?.Count > 0)
			{
				var subItem = new MenuFlyoutSubItem { Text = item.Label, Icon = CreateIcon(item) };
				if (item.ItemType == ToolbarItemTypes.SplitButton && item.Command is not null)
				{
					subItem.Items.Add(CreateCommandMenuItem(item));
				}

				if (item.SubItems is not null)
				{
					AddMenuItems(item.SubItems, subItem.Items);
				}

				return subItem;
			}

			return CreateCommandMenuItem(item);
		}

		private static MenuFlyoutItem CreateCommandMenuItem(ToolbarItem item)
		{
			return new MenuFlyoutItem
			{
				Text = item.Label,
				Command = item.Command,
				CommandParameter = item.CommandParameter,
				KeyboardAcceleratorTextOverride = item.KeyboardAcceleratorTextOverride,
				Icon = CreateIcon(item),
			};
		}

		private void ItemTemplateChanged(DataTemplate? newDataTemplate)
		{
			if (_itemsRepeater is not null)
			{
				_itemsRepeater.ItemTemplate = newDataTemplate;
			}
		}

		private void ToolbarSizeChanged(ToolbarSizes newToolbarSize)
		{
			UpdateMinSizesFromResources();
			foreach (var materializedItem in _materializedItems)
			{
				ApplyToolbarItem(materializedItem.Element, materializedItem.Item);
			}

			RequestLayoutUpdate();
		}

		private void UpdateMinSizesFromResources()
		{
			_smallMinWidth = GetResourceDouble(SmallMinWidthResourceKey, _smallMinWidth);
			_smallMinHeight = GetResourceDouble(SmallMinHeightResourceKey, _smallMinHeight);
			_mediumMinWidth = GetResourceDouble(MediumMinWidthResourceKey, _mediumMinWidth);
			_mediumMinHeight = GetResourceDouble(MediumMinHeightResourceKey, _mediumMinHeight);
			_largeMinWidth = GetResourceDouble(LargeMinWidthResourceKey, _largeMinWidth);
			_largeMinHeight = GetResourceDouble(LargeMinHeightResourceKey, _largeMinHeight);

			(_currentMinWidth, _currentMinHeight) = ToolbarSize switch
			{
				ToolbarSizes.Small => (_smallMinWidth, _smallMinHeight),
				ToolbarSizes.Large => (_largeMinWidth, _largeMinHeight),
				_ => (_mediumMinWidth, _mediumMinHeight),
			};
		}

		private static double GetResourceDouble(string key, double fallback)
		{
			var resources = Application.Current?.Resources;
			if (resources is not null && resources.TryGetValue(key, out var value) && value is double resourceValue && double.IsFinite(resourceValue))
			{
				return resourceValue;
			}

			return fallback;
		}

		private void RequestLayoutUpdate()
		{
			if (!_templateApplied || _itemsRepeater is null || _layoutUpdateQueued)
			{
				return;
			}

			_layoutUpdateQueued = true;
			if (DispatcherQueue is null || !DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, UpdateLayoutPartition))
			{
				_layoutUpdateQueued = false;
			}
		}

		private void UpdateLayoutPartition()
		{
			_layoutUpdateQueued = false;
			if (!_templateApplied || _itemsRepeater is null)
			{
				return;
			}

			var spacing = GetItemSpacing();
			var availableWidth = GetAvailableContentWidth();
			var infiniteSize = new Size(double.PositiveInfinity, double.PositiveInfinity);
			foreach (var materializedItem in _materializedItems)
			{
				materializedItem.Element.Measure(infiniteSize);
				materializedItem.Width = Math.Max(materializedItem.Element.DesiredSize.Width, materializedItem.Element.MinWidth);
			}

			var overflowWidth = GetOverflowWidth(infiniteSize);
			var hasAlwaysItems = _materializedItems.Any(x => x.Item.OverflowBehavior == OverflowBehaviors.Always);
			var allNonAlwaysWidth = GetMaterializedWidth(x => x.Item.OverflowBehavior != OverflowBehaviors.Always, spacing);
			var visibleItems = new List<IToolbarItemSet>();
			var overflowItems = new List<ToolbarItem>();

			if (!hasAlwaysItems && allNonAlwaysWidth <= availableWidth)
			{
				foreach (var materializedItem in _materializedItems)
				{
					if (materializedItem.Item.OverflowBehavior != OverflowBehaviors.Always)
					{
						visibleItems.Add(materializedItem.Element as IToolbarItemSet ?? throw new InvalidOperationException("Toolbar items must implement IToolbarItemSet."));
					}
				}
			}
			else
			{
				var availableForItems = Math.Max(0, availableWidth - overflowWidth - spacing);
				var requiredWidth = GetMaterializedWidth(x => x.Item.OverflowBehavior == OverflowBehaviors.Never, spacing);
				var optionalWidth = Math.Max(0, availableForItems - requiredWidth);
				var optionalCount = 0;
				var visibleSet = new HashSet<ToolbarItem>();

				foreach (var materializedItem in _materializedItems)
				{
					if (materializedItem.Item.OverflowBehavior == OverflowBehaviors.Never)
					{
						visibleSet.Add(materializedItem.Item);
						continue;
					}

					if (materializedItem.Item.OverflowBehavior == OverflowBehaviors.Always)
					{
						overflowItems.Add(materializedItem.Item);
						continue;
					}

					var candidateWidth = materializedItem.Width + ((requiredWidth > 0 || optionalCount > 0) ? spacing : 0);
					if (candidateWidth <= optionalWidth)
					{
						optionalWidth -= candidateWidth;
						optionalCount++;
						visibleSet.Add(materializedItem.Item);
					}
					else
					{
						overflowItems.Add(materializedItem.Item);
					}
				}

				foreach (var materializedItem in _materializedItems)
				{
					if (visibleSet.Contains(materializedItem.Item))
					{
						visibleItems.Add(materializedItem.Element as IToolbarItemSet ?? throw new InvalidOperationException("Toolbar items must implement IToolbarItemSet."));
					}
				}
			}

			if (!_visibleItems.SequenceEqual(visibleItems))
			{
				_visibleItems = visibleItems;
				_itemsRepeater.ItemsSource = _visibleItems;
			}

			var overflowChanged = !_overflowItems.SequenceEqual(overflowItems);
			_overflowItems = overflowItems;
			if (_overflowItems.Count > 0)
			{
				if (_overflowStackPanel is not null)
				{
					_overflowStackPanel.Visibility = Visibility.Visible;
				}

				if (_overflowButton is not null)
				{
					_overflowButton.Visibility = Visibility.Visible;
				}

				if (overflowChanged || _overflowMenuDirty)
				{
					PopulateOverflowMenu(_overflowItems);
				}
			}
			else
			{
				if (_overflowStackPanel is not null)
				{
					_overflowStackPanel.Visibility = Visibility.Collapsed;
				}

				if (_overflowButton is not null)
				{
					_overflowButton.Visibility = Visibility.Collapsed;
				}
			}
		}

		private void PopulateOverflowMenu(IReadOnlyList<ToolbarItem> items)
		{
			if (_overflowFlyout is null)
			{
				return;
			}

			_overflowFlyout.Items.Clear();
			AddMenuItems(items, _overflowFlyout.Items);
			_overflowMenuDirty = false;
		}

		private double GetMaterializedWidth(Func<MaterializedToolbarItem, bool> predicate, double spacing)
		{
			var width = 0d;
			var count = 0;
			foreach (var materializedItem in _materializedItems)
			{
				if (predicate(materializedItem))
				{
					width += materializedItem.Width;
					count++;
				}
			}

			return count == 0 ? 0 : width + ((count - 1) * spacing);
		}

		private double GetOverflowWidth(Size infiniteSize)
		{
			if (_overflowStackPanel is not null)
			{
				var previousVisibility = _overflowStackPanel.Visibility;
				_overflowStackPanel.Visibility = Visibility.Visible;
				_overflowStackPanel.Measure(infiniteSize);
				var desiredWidth = _overflowStackPanel.DesiredSize.Width;
				_overflowStackPanel.Visibility = previousVisibility;
				if (desiredWidth > 0)
				{
					return desiredWidth;
				}
			}

			return Math.Max(_overflowButton?.MinWidth ?? 0, DefaultOverflowButtonWidth);
		}

		private double GetAvailableContentWidth()
		{
			if (!double.IsFinite(ActualWidth))
			{
				return double.PositiveInfinity;
			}

			var horizontalChrome = Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right;
			return Math.Max(0, ActualWidth - horizontalChrome);
		}

		private static double GetItemSpacing()
		{
			var resources = Application.Current?.Resources;
			if (resources is not null && resources.TryGetValue("ToolbarItemSpacing", out var value) && value is double spacing && double.IsFinite(spacing) && spacing >= 0)
			{
				return spacing;
			}

			return DefaultItemSpacing;
		}

		private sealed class MaterializedToolbarItem
		{
			public MaterializedToolbarItem(ToolbarItem item, FrameworkElement element)
			{
				Item = item;
				Element = element;
			}

			public ToolbarItem Item { get; }

			public FrameworkElement Element { get; }

			public double Width { get; set; }
		}

		private sealed class ToolbarContentHost : ContentControl, IToolbarItemSet
		{
			public ToolbarContentHost()
			{
				HorizontalContentAlignment = HorizontalAlignment.Center;
				VerticalContentAlignment = VerticalAlignment.Center;
			}
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Controls;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed partial class BrowseItemViewModel : ObservableObject, ITableViewCellValueProvider
{
	private const string ItemNamePropertyId = "System.ItemNameDisplay";
	private const string ItemTypeTextPropertyId = "System.ItemTypeText";
	private const string ReferencePropertyId = "reference";
	internal static readonly IReadOnlyDictionary<string, object?> EmptyProperties = new Dictionary<string, object?>(StringComparer.Ordinal);

	private IReadOnlyDictionary<string, object?> _properties = EmptyProperties;

	public string Name { get; private set; }

	public string DisplayName { get; private set; }

	public bool IsFolder { get; private set; }

	public bool IsHidden { get; private set; }

	public StorableReference Reference { get; private set; }

	[ObservableProperty]
	public partial BitmapImage? Thumbnail { get; set; }

	public string Kind => (IsFolder ? Strings.Folder : Strings.File).GetLocalized();

	public double IconOpacity => IsHidden ? 0.4 : 1;

	public double DefaultIconOpacity => IsHidden ? 0.4 : 0.45;

	public Visibility DefaultIconVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

	public string ReferenceText => Reference.LastKnownAddress?.Value ?? Reference.ItemId;

	public IReadOnlyDictionary<string, object?> Properties => _properties;

	internal BrowseItemLayoutMetrics LayoutMetrics { get; private set; }

	public BrowseItemViewModel(string name, bool isFolder, StorableReference reference, bool isHidden = false, bool showFileExtensions = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(reference);

		Name = name;
		DisplayName = GetDisplayName(name, isFolder, showFileExtensions);
		IsFolder = isFolder;
		IsHidden = isHidden;
		Reference = reference;
		LayoutMetrics = new BrowseItemLayoutMetrics();
	}

	internal void SetThumbnail(BitmapImage? value)
	{
		Thumbnail = value;
	}

	internal void SetLayoutMetrics(BrowseItemLayoutMetrics value)
	{
		ArgumentNullException.ThrowIfNull(value);

		LayoutMetrics = value;
	}

	partial void OnThumbnailChanged(BitmapImage? value)
	{
		OnPropertyChanged(nameof(DefaultIconVisibility));
	}

	internal void SetProperties(IReadOnlyDictionary<string, object?> value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (ReferenceEquals(_properties, value))
		{
			return;
		}

		_properties = value;
		OnPropertyChanged(nameof(Properties));
	}

	internal void UpdateModel(IStorableModel item, bool showFileExtensions)
	{
		ArgumentNullException.ThrowIfNull(item);

		var nameChanged = !string.Equals(Name, item.Name, StringComparison.Ordinal);
		var folderChanged = IsFolder != (item is IFolderModel);
		var hiddenChanged = IsHidden != item.IsHidden;
		var referenceChanged = !Equals(Reference, item.Reference);
		var displayName = GetDisplayName(item.Name, item is IFolderModel, showFileExtensions);
		var displayNameChanged = !string.Equals(DisplayName, displayName, StringComparison.Ordinal);
		Name = item.Name;
		DisplayName = displayName;
		IsFolder = item is IFolderModel;
		IsHidden = item.IsHidden;
		Reference = item.Reference;
		if (nameChanged)
		{
			OnPropertyChanged(nameof(Name));
		}

		if (displayNameChanged)
		{
			OnPropertyChanged(nameof(DisplayName));
		}

		if (folderChanged)
		{
			OnPropertyChanged(nameof(IsFolder));
			OnPropertyChanged(nameof(Kind));
		}

		if (hiddenChanged)
		{
			OnPropertyChanged(nameof(IsHidden));
			OnPropertyChanged(nameof(IconOpacity));
			OnPropertyChanged(nameof(DefaultIconOpacity));
		}

		if (referenceChanged)
		{
			OnPropertyChanged(nameof(Reference));
			OnPropertyChanged(nameof(ReferenceText));
		}
	}

	/// <inheritdoc />
	public string GetDisplayText(string propertyId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		if (propertyId.Equals("name", StringComparison.OrdinalIgnoreCase) || propertyId.Equals(ItemNamePropertyId, StringComparison.Ordinal))
		{
			return DisplayName;
		}

		if (propertyId.Equals(ReferencePropertyId, StringComparison.OrdinalIgnoreCase))
		{
			return ReferenceText;
		}

		if (_properties.TryGetValue(propertyId, out var value))
		{
			var text = FormatPropertyValue(value);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}

		if (propertyId.Equals(ItemTypeTextPropertyId, StringComparison.Ordinal))
		{
			return Kind;
		}

		return string.Empty;
	}

	private static string? FormatPropertyValue(object? value)
	{
		if (value is null)
		{
			return null;
		}

		if (value is string text)
		{
			return text;
		}

		if (value is DateTimeOffset dateTimeOffset)
		{
			return dateTimeOffset.ToString("g", CultureInfo.CurrentCulture);
		}

		if (value is DateTime dateTime)
		{
			return dateTime.ToString("g", CultureInfo.CurrentCulture);
		}

		if (value is IFormattable formattable)
		{
			return formattable.ToString(null, CultureInfo.CurrentCulture);
		}

		return value.ToString();
	}

	private static string GetDisplayName(string name, bool isFolder, bool showFileExtensions)
	{
		if (isFolder || showFileExtensions)
		{
			return name;
		}

		var extension = Path.GetExtension(name);
		if (extension.Length is 0)
		{
			return name;
		}

		return name[..^extension.Length];
	}
}

internal sealed class BrowseItemLayoutMetrics : ObservableObject
{
	private double _layoutSize;

	public double LayoutSize => _layoutSize;

	public double DetailsRowHeight => 28 + ((LayoutSize - 1) * 8);

	public double ListThumbnailSize => 24 + ((LayoutSize - 1) * 8);

	public double ListItemHeight => ListThumbnailSize + 12;

	public double CardsThumbnailSize => 48 + ((LayoutSize - 1) * 12);

	public double CardsItemHeight => CardsThumbnailSize + 24;

	public double GridItemSize => 104 + ((LayoutSize - 1) * 28);

	public double GridThumbnailSize => GridItemSize - 44;

	public double GridDefaultIconSize => GridThumbnailSize * 0.57;

	internal BrowseItemLayoutMetrics(double? itemSize = null)
	{
		_layoutSize = NormalizeLayoutSize(itemSize);
	}

	internal void Update(double? itemSize)
	{
		if (!SetProperty(ref _layoutSize, NormalizeLayoutSize(itemSize), nameof(LayoutSize)))
		{
			return;
		}

		OnPropertyChanged(nameof(DetailsRowHeight));
		OnPropertyChanged(nameof(ListThumbnailSize));
		OnPropertyChanged(nameof(ListItemHeight));
		OnPropertyChanged(nameof(CardsThumbnailSize));
		OnPropertyChanged(nameof(CardsItemHeight));
		OnPropertyChanged(nameof(GridItemSize));
		OnPropertyChanged(nameof(GridThumbnailSize));
		OnPropertyChanged(nameof(GridDefaultIconSize));
	}

	private static double NormalizeLayoutSize(double? itemSize)
	{
		return Math.Clamp(Math.Round(itemSize ?? 3), 1, 5);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Localization;
using Files.Core.Storage;
using System.Globalization;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Files.ViewModels;

public sealed partial class BrowseItemViewModel : ObservableObject
{
	private const string ItemNamePropertyId = "System.ItemNameDisplay";
	private const string ItemTypeTextPropertyId = "System.ItemTypeText";
	private const string ReferencePropertyId = "reference";

	private IReadOnlyDictionary<string, object?> _properties = new Dictionary<string, object?>(StringComparer.Ordinal);

	public string Name { get; }

	public bool IsFolder { get; }

	public StorableReference Reference { get; }

	[ObservableProperty]
	public partial BitmapImage? Thumbnail { get; set; }

	public string Kind => (IsFolder ? Strings.Folder : Strings.File).GetLocalized();

	public string ReferenceText => Reference.LastKnownAddress?.Value ?? Reference.ItemId;

	public IReadOnlyDictionary<string, object?> Properties => _properties;

	public BrowseItemViewModel(string name, bool isFolder, StorableReference reference)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(reference);

		Name = name;
		IsFolder = isFolder;
		Reference = reference;
	}

	internal void SetThumbnail(BitmapImage? value)
	{
		Thumbnail = value;
	}

	internal void SetProperties(IReadOnlyDictionary<string, object?> value)
	{
		ArgumentNullException.ThrowIfNull(value);

		_properties = value;
		OnPropertyChanged(nameof(Properties));
	}

	internal string GetDisplayText(string propertyId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		if (propertyId.Equals("name", StringComparison.OrdinalIgnoreCase) || propertyId.Equals(ItemNamePropertyId, StringComparison.Ordinal))
		{
			return Name;
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
}

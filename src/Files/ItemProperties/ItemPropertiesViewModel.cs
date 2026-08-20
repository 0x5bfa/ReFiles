// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.IO;
using Files.Localization;
using Files.ViewModels;

namespace Files.ItemProperties;

internal sealed class ItemPropertiesViewModel
{
	public string WindowTitle { get; }

	public string Name { get; }

	public string Type { get; }

	public string Location { get; }

	public string Size { get; }

	public string DateCreated { get; }

	public string DateModified { get; }

	public string TypeLabel { get; } = Strings.Type.GetLocalized();

	public string LocationLabel { get; } = Strings.Location.GetLocalized();

	public string SizeLabel { get; } = Strings.Size.GetLocalized();

	public string DateCreatedLabel { get; } = Strings.DateCreated.GetLocalized();

	public string DateModifiedLabel { get; } = Strings.DateModified.GetLocalized();

	public string CloseLabel { get; } = Strings.Close.GetLocalized();

	public ItemPropertiesViewModel(IReadOnlyList<BrowseItemViewModel> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (items.Count is 0)
		{
			throw new ArgumentException("At least one item is required.", nameof(items));
		}

		var unspecified = Strings.Unspecified.GetLocalized();
		Name = items.Count is 1 ? items[0].DisplayName : FormatItemCount(items.Count);
		WindowTitle = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesTitleFormat.GetLocalized(), Name);
		Type = items.Count is 1 ? GetValue(items[0], BrowseDisplayPropertyIds.Type, items[0].Kind) : Strings.MultipleTypes.GetLocalized();
		Location = GetLocation(items) ?? unspecified;
		Size = GetSize(items) ?? unspecified;
		DateCreated = items.Count is 1 ? GetValue(items[0], BrowseDisplayPropertyIds.DateCreated, unspecified) : unspecified;
		DateModified = items.Count is 1 ? GetValue(items[0], BrowseDisplayPropertyIds.DateModified, unspecified) : unspecified;
	}

	private static string FormatItemCount(int count)
	{
		var format = count is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized();

		return string.Format(CultureInfo.CurrentCulture, format, count);
	}

	private static string GetValue(BrowseItemViewModel item, string propertyId, string fallback)
	{
		var value = item.GetDisplayText(propertyId);

		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	private static string? GetLocation(IReadOnlyList<BrowseItemViewModel> items)
	{
		string? location = null;
		foreach (var item in items)
		{
			var value = item.Reference.LastKnownAddress?.Value;
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			var itemLocation = Path.IsPathRooted(value) ? Path.GetDirectoryName(value) : value;
			if (string.IsNullOrWhiteSpace(itemLocation))
			{
				return null;
			}

			if (location is not null && !string.Equals(location, itemLocation, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			location = itemLocation;
		}

		return location;
	}

	private static string? GetSize(IReadOnlyList<BrowseItemViewModel> items)
	{
		ulong total = 0;
		foreach (var item in items)
		{
			if (!item.Properties.TryGetValue(BrowseDisplayPropertyIds.Size, out var value) || !TryGetUInt64(value, out var size))
			{
				return null;
			}

			total = checked(total + size);
		}

		return FormatSize(total);
	}

	private static bool TryGetUInt64(object? value, out ulong result)
	{
		try
		{
			result = Convert.ToUInt64(value, CultureInfo.InvariantCulture);

			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
		{
			result = 0;

			return false;
		}
	}

	private static string FormatSize(ulong size)
	{
		string[] suffixes =
		[
			Strings.ByteSymbol.GetLocalized(),
			Strings.KilobyteSymbol.GetLocalized(),
			Strings.MegabyteSymbol.GetLocalized(),
			Strings.GigabyteSymbol.GetLocalized(),
			Strings.TerabyteSymbol.GetLocalized(),
			Strings.PetabyteSymbol.GetLocalized(),
		];
		var value = (double)size;
		var suffixIndex = 0;
		while (value >= 1024 && suffixIndex < suffixes.Length - 1)
		{
			value /= 1024;
			suffixIndex++;
		}

		return suffixIndex is 0
			? string.Format(CultureInfo.CurrentCulture, Strings.SizeBytesFormat.GetLocalized(), size, suffixes[suffixIndex])
			: string.Format(CultureInfo.CurrentCulture, Strings.SizeScaledFormat.GetLocalized(), value, suffixes[suffixIndex], size, suffixes[0]);
	}
}

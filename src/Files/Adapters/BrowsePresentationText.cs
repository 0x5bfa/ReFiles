// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Files.Localization;

namespace Files.Adapters;

internal sealed record BrowsePresentationText(string Home, BrowseStatusBarText StatusBar, string NotFolderFormat)
{
	public static BrowsePresentationText CreateLocalized()
	{
		return new BrowsePresentationText(
			Strings.Home.GetLocalized(),
			new BrowseStatusBarText(
				Strings.ItemCountSingle.GetLocalized(),
				Strings.ItemCountPlural.GetLocalized(),
				Strings.SelectedItemCountSingle.GetLocalized(),
				Strings.SelectedItemCountPlural.GetLocalized(),
				Strings.SelectedItemsWithSizeFormat.GetLocalized(),
				Strings.StorageOperationSizeFormat.GetLocalized(),
				[
					Strings.ByteSymbol.GetLocalized(),
					Strings.KilobyteSymbol.GetLocalized(),
					Strings.MegabyteSymbol.GetLocalized(),
					Strings.GigabyteSymbol.GetLocalized(),
					Strings.TerabyteSymbol.GetLocalized(),
					Strings.PetabyteSymbol.GetLocalized(),
				]),
			Strings.NotFolderFormat.GetLocalized());
	}
}

internal sealed record BrowseStatusBarText(
	string ItemCountSingle,
	string ItemCountPlural,
	string SelectedItemCountSingle,
	string SelectedItemCountPlural,
	string SelectedItemsWithSizeFormat,
	string SizeFormat,
	IReadOnlyList<string> SizeSuffixes)
{
	public string FormatItemCount(int count)
	{
		return string.Format(CultureInfo.CurrentCulture, count is 1 ? ItemCountSingle : ItemCountPlural, count);
	}

	public string FormatSelectedItemCount(int count, ulong? size)
	{
		var countText = string.Format(CultureInfo.CurrentCulture, count is 1 ? SelectedItemCountSingle : SelectedItemCountPlural, count);
		if (size is null)
		{
			return countText;
		}

		return string.Format(CultureInfo.CurrentCulture, SelectedItemsWithSizeFormat, countText, FormatSize(size.Value));
	}

	private string FormatSize(ulong size)
	{
		var value = (double)size;
		var suffixIndex = 0;
		while (value >= 1024 && suffixIndex < SizeSuffixes.Count - 1)
		{
			value /= 1024;
			suffixIndex++;
		}

		var valueText = suffixIndex is 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString(value < 10 ? "N2" : value < 100 ? "N1" : "N0", CultureInfo.CurrentCulture);

		return string.Format(CultureInfo.CurrentCulture, SizeFormat, valueText, SizeSuffixes[suffixIndex]);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Files.Core.Capabilities.Properties;
using Files.Core.ViewSettings;
using Files.Localization;

namespace Files.ViewModels;

internal static class BrowseDisplayPropertyIds
{
	public const string Name = "System.ItemNameDisplay";
	public const string DateModified = "System.DateModified";
	public const string DateCreated = "System.DateCreated";
	public const string Size = "System.Size";
	public const string Type = "System.ItemTypeText";
}

public sealed class BrowseItemGroupViewModel : ObservableCollection<BrowseItemViewModel>
{
	internal BrowseItemGroupKind Kind { get; }

	internal object SortValue { get; }

	public string Title { get; }

	public string CountText { get; }

	internal BrowseItemGroupViewModel(string title, string countText, BrowseItemGroupKind kind, object sortValue, IEnumerable<BrowseItemViewModel> items)
		: base(items)
	{
		Title = title;
		CountText = countText;
		Kind = kind;
		SortValue = sortValue;
	}
}

internal enum BrowseItemGroupKind
{
	Normal,
	Folder,
	Missing,
}

internal sealed record BrowseGroupingText(
	string Folders,
	string Files,
	string Unspecified,
	string SizeTiny,
	string SizeSmall,
	string SizeMedium,
	string SizeLarge,
	string SizeVeryLarge,
	string SizeHuge)
{
	public static BrowseGroupingText CreateLocalized()
	{
		return new(
			Strings.Folders.GetLocalized(),
			Strings.Files.GetLocalized(),
			Strings.Unspecified.GetLocalized(),
			Strings.SizeGroupTiny.GetLocalized(),
			Strings.SizeGroupSmall.GetLocalized(),
			Strings.SizeGroupMedium.GetLocalized(),
			Strings.SizeGroupLarge.GetLocalized(),
			Strings.SizeGroupVeryLarge.GetLocalized(),
			Strings.SizeGroupHuge.GetLocalized());
	}
}

internal static class BrowseItemGrouping
{
	private const ulong Kilobyte = 1024;
	private const ulong Megabyte = 1024 * Kilobyte;
	private const ulong Gigabyte = 1024 * Megabyte;

	public static IReadOnlyList<BrowseItemGroupViewModel> Create(IEnumerable<BrowseItemViewModel> items, string propertyId, ViewSortDirection direction, BrowseGroupingText? text = null)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
		if (!Enum.IsDefined(direction))
		{
			throw new ArgumentOutOfRangeException(nameof(direction));
		}

		var groupingText = text ?? BrowseGroupingText.CreateLocalized();
		var groups = items.GroupBy(item => CreateKey(item, propertyId, groupingText)).Select(group => CreateGroup(group.Key, group, groupingText)).ToList();
		groups.Sort((left, right) => CompareGroups(left, right, direction));

		return Array.AsReadOnly(groups.ToArray());
	}

	private static BrowseItemGroupViewModel CreateGroup(GroupKey key, IEnumerable<BrowseItemViewModel> items, BrowseGroupingText text)
	{
		var groupItems = items.ToArray();
		var itemLabel = key.Kind is BrowseItemGroupKind.Folder ? text.Folders : text.Files;
		var countLabel = groupItems.Length == 1 ? itemLabel.TrimEnd('s') : itemLabel;
		var countText = string.Format(CultureInfo.CurrentCulture, "{0} {1}", groupItems.Length, countLabel);

		return new BrowseItemGroupViewModel(key.Title, countText, key.Kind, key.SortValue, groupItems);
	}

	private static GroupKey CreateKey(BrowseItemViewModel item, string propertyId, BrowseGroupingText text)
	{
		if (propertyId.Equals(BrowseDisplayPropertyIds.Name, StringComparison.Ordinal) || propertyId.Equals("name", StringComparison.OrdinalIgnoreCase))
		{
			var title = StringInfo.GetNextTextElement(item.Name).ToUpper(CultureInfo.CurrentCulture);

			return new(BrowseItemGroupKind.Normal, title, title);
		}

		if (propertyId.Equals(BrowseDisplayPropertyIds.Size, StringComparison.Ordinal))
		{
			return CreateSizeKey(item, text);
		}

		if (propertyId.Equals(BrowseDisplayPropertyIds.Type, StringComparison.Ordinal))
		{
			return CreateTypeKey(item, text);
		}

		if (propertyId.Equals(BrowseDisplayPropertyIds.DateModified, StringComparison.Ordinal) || propertyId.Equals(BrowseDisplayPropertyIds.DateCreated, StringComparison.Ordinal))
		{
			return CreateDateKey(item, propertyId, text);
		}

		return CreateValueKey(item, propertyId, text);
	}

	private static GroupKey CreateDateKey(BrowseItemViewModel item, string propertyId, BrowseGroupingText text)
	{
		if (!item.Properties.TryGetValue(propertyId, out var value))
		{
			return MissingKey(text);
		}

		var localDate = GetRawValue(value) switch
		{
			DateTimeOffset dateTimeOffset => dateTimeOffset.ToLocalTime().Date,
			DateTime dateTime => dateTime.ToLocalTime().Date,
			_ => (DateTime?)null,
		};
		if (localDate is not { } date)
		{
			return MissingKey(text);
		}

		return new(BrowseItemGroupKind.Normal, date.ToString("D", CultureInfo.CurrentCulture), date);
	}

	private static GroupKey CreateSizeKey(BrowseItemViewModel item, BrowseGroupingText text)
	{
		if (item.IsFolder)
		{
			return new(BrowseItemGroupKind.Folder, text.Folders, 0);
		}

		if (!item.Properties.TryGetValue(BrowseDisplayPropertyIds.Size, out var value) || !TryGetSize(GetRawValue(value), out var size))
		{
			return MissingKey(text);
		}

		return size switch
		{
			<= 16 * Kilobyte => new(BrowseItemGroupKind.Normal, text.SizeTiny, 0),
			<= Megabyte => new(BrowseItemGroupKind.Normal, text.SizeSmall, 1),
			<= 128 * Megabyte => new(BrowseItemGroupKind.Normal, text.SizeMedium, 2),
			<= Gigabyte => new(BrowseItemGroupKind.Normal, text.SizeLarge, 3),
			<= 4 * Gigabyte => new(BrowseItemGroupKind.Normal, text.SizeVeryLarge, 4),
			_ => new(BrowseItemGroupKind.Normal, text.SizeHuge, 5),
		};
	}

	private static GroupKey CreateTypeKey(BrowseItemViewModel item, BrowseGroupingText text)
	{
		if (item.IsFolder)
		{
			return new(BrowseItemGroupKind.Folder, text.Folders, string.Empty);
		}

		if (item.Properties.TryGetValue(BrowseDisplayPropertyIds.Type, out var value) && value is not null && !string.IsNullOrWhiteSpace(GetDisplayText(value)))
		{
			var title = GetDisplayText(value)!;

			return new(BrowseItemGroupKind.Normal, title, GetRawValue(value) ?? title);
		}

		var extension = Path.GetExtension(item.Name);
		if (!string.IsNullOrWhiteSpace(extension))
		{
			var title = extension.TrimStart('.').ToUpper(CultureInfo.CurrentCulture);

			return new(BrowseItemGroupKind.Normal, title, title);
		}

		return MissingKey(text);
	}

	private static GroupKey CreateValueKey(BrowseItemViewModel item, string propertyId, BrowseGroupingText text)
	{
		if (!item.Properties.TryGetValue(propertyId, out var value) || value is null)
		{
			return MissingKey(text);
		}

		var title = GetDisplayText(value);
		if (string.IsNullOrWhiteSpace(title))
		{
			return MissingKey(text);
		}

		return new(BrowseItemGroupKind.Normal, title, GetRawValue(value) ?? title);
	}

	private static object? GetRawValue(object? value)
	{
		return value is FormattedPropertyValue formattedValue ? formattedValue.RawValue : value;
	}

	private static string? GetDisplayText(object value)
	{
		return value is FormattedPropertyValue formattedValue ? formattedValue.DisplayText : Convert.ToString(value, CultureInfo.CurrentCulture);
	}

	private static GroupKey MissingKey(BrowseGroupingText text)
	{
		return new(BrowseItemGroupKind.Missing, text.Unspecified, string.Empty);
	}

	private static int CompareGroups(BrowseItemGroupViewModel left, BrowseItemGroupViewModel right, ViewSortDirection direction)
	{
		var kindComparison = GetKindOrder(left.Kind).CompareTo(GetKindOrder(right.Kind));
		if (kindComparison is not 0)
		{
			return kindComparison;
		}

		var comparison = CompareValues(left.SortValue, right.SortValue);

		return direction is ViewSortDirection.Ascending ? comparison : -comparison;
	}

	private static int CompareValues(object left, object right)
	{
		if (left.GetType() == right.GetType() && left is IComparable comparable)
		{
			try
			{
				return comparable.CompareTo(right);
			}
			catch (ArgumentException)
			{
			}
		}

		return StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(left, CultureInfo.CurrentCulture), Convert.ToString(right, CultureInfo.CurrentCulture));
	}

	private static int GetKindOrder(BrowseItemGroupKind kind)
	{
		return kind switch
		{
			BrowseItemGroupKind.Folder => 0,
			BrowseItemGroupKind.Normal => 1,
			BrowseItemGroupKind.Missing => 2,
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
	}

	private static bool TryGetSize(object? value, out ulong size)
	{
		try
		{
			size = Convert.ToUInt64(value, CultureInfo.InvariantCulture);

			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
		{
			size = 0;

			return false;
		}
	}

	private sealed record GroupKey(BrowseItemGroupKind Kind, string Title, object SortValue);
}

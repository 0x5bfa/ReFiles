// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;
using System.Globalization;

namespace Files.Core.Browsing;

internal sealed class BrowseItemProjection
{
	private const string ItemNamePropertyId = "System.ItemNameDisplay";

	private readonly Dictionary<StorableKey, IStorableModel> modelsByKey = [];
	private readonly List<IStorableModel> orderedItems = [];
	private readonly Func<IStorableModel, string, object?>? propertyValueGetter;
	private IReadOnlyList<IStorableModel> orderedItemsSnapshot =
		Array.Empty<IStorableModel>();
	private IComparer<IStorableModel> comparer;

	public BrowseItemProjection(
		BrowseViewSettings settings,
		Func<IStorableModel, string, object?>? propertyValueGetter = null)
	{
		ArgumentNullException.ThrowIfNull(settings);
		this.propertyValueGetter = propertyValueGetter;
		comparer = CreateComparer(settings, propertyValueGetter);
	}

	public IReadOnlyList<IStorableModel> Items =>
		Volatile.Read(ref orderedItemsSnapshot);

	public bool TryGet(
		StorableKey key,
		out IStorableModel model,
		out int index)
	{
		if (!modelsByKey.TryGetValue(key, out var foundModel))
		{
			model = null!;
			index = -1;
			return false;
		}

		model = foundModel;
		index = FindItemIndex(key);
		if (index < 0)
		{
			throw new InvalidOperationException("The item projection is inconsistent.");
		}

		return true;
	}

	public BrowseItemChangeSet Add(IStorableModel model)
	{
		ArgumentNullException.ThrowIfNull(model);

		var key = model.Reference.GetKey();
		if (modelsByKey.ContainsKey(key))
		{
			return BrowseItemChangeSet.Empty;
		}

		var index = FindInsertionIndex(model);
		modelsByKey.Add(key, model);
		orderedItems.Insert(index, model);
		UpdateSnapshot();

		return new BrowseItemChangeSet([
			new BrowseItemAdded(index, model)]);
	}

	public BrowseItemChangeSet Remove(StorableKey key)
	{
		if (!modelsByKey.Remove(key, out _))
		{
			return BrowseItemChangeSet.Empty;
		}

		var index = FindItemIndex(key);
		if (index < 0)
		{
			throw new InvalidOperationException("The item projection is inconsistent.");
		}

		orderedItems.RemoveAt(index);
		UpdateSnapshot();

		return new BrowseItemChangeSet([
			new BrowseItemRemoved(index, key)]);
	}

	public BrowseItemChangeSet Replace(
		StorableKey previousKey,
		IStorableModel replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);

		if (!modelsByKey.ContainsKey(previousKey))
		{
			throw new InvalidOperationException("The item to replace does not exist.");
		}

		var replacementKey = replacement.Reference.GetKey();
		if (replacementKey != previousKey && modelsByKey.ContainsKey(replacementKey))
		{
			throw new InvalidOperationException("The replacement key already exists.");
		}

		var previousIndex = FindItemIndex(previousKey);
		if (previousIndex < 0)
		{
			throw new InvalidOperationException("The item projection is inconsistent.");
		}

		if (replacementKey == previousKey)
		{
			modelsByKey[previousKey] = replacement;
		}
		else
		{
			modelsByKey.Remove(previousKey);
			modelsByKey.Add(replacementKey, replacement);
		}

		orderedItems[previousIndex] = replacement;
		orderedItems.Sort(comparer);
		var currentIndex = FindItemIndex(replacementKey);
		UpdateSnapshot();

		var changes = new List<BrowseItemChange>
		{
			new BrowseItemReplaced(previousIndex, previousKey, replacement),
		};
		if (previousIndex != currentIndex)
		{
			changes.Add(new BrowseItemMoved(
				previousIndex,
				currentIndex,
				replacementKey));
		}

		return new BrowseItemChangeSet(changes);
	}

	public BrowseItemChangeSet Reset(IEnumerable<IStorableModel> models)
	{
		ArgumentNullException.ThrowIfNull(models);

		var nextModels = models.ToList();
		var nextByKey = new Dictionary<StorableKey, IStorableModel>();
		foreach (var model in nextModels)
		{
			ArgumentNullException.ThrowIfNull(model);
			if (!nextByKey.TryAdd(model.Reference.GetKey(), model))
			{
				throw new InvalidOperationException("The item projection contains duplicate keys.");
			}
		}

		orderedItems.Clear();
		orderedItems.AddRange(nextModels);
		orderedItems.Sort(comparer);
		modelsByKey.Clear();
		foreach (var pair in nextByKey)
		{
			modelsByKey.Add(pair.Key, pair.Value);
		}

		UpdateSnapshot();
		return new BrowseItemChangeSet([
			new BrowseItemsReset(Items)]);
	}

	public BrowseItemChangeSet UpdateSort(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		var previousKeys = orderedItems
			.Select(static item => item.Reference.GetKey())
			.ToArray();
		comparer = CreateComparer(settings, propertyValueGetter);
		orderedItems.Sort(comparer);
		if (previousKeys.SequenceEqual(
			orderedItems.Select(static item => item.Reference.GetKey())))
		{
			return BrowseItemChangeSet.Empty;
		}

		UpdateSnapshot();
		return new BrowseItemChangeSet([
			new BrowseItemsReset(Items)]);
	}

	private int FindInsertionIndex(IStorableModel model)
	{
		var low = 0;
		var high = orderedItems.Count;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (comparer.Compare(orderedItems[middle], model) <= 0)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	private int FindItemIndex(StorableKey key)
	{
		for (var index = 0; index < orderedItems.Count; index++)
		{
			if (orderedItems[index].Reference.GetKey() == key)
			{
				return index;
			}
		}

		return -1;
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(
			ref orderedItemsSnapshot,
			Array.AsReadOnly(orderedItems.ToArray()));
	}

	private static IComparer<IStorableModel> CreateComparer(
		BrowseViewSettings settings,
		Func<IStorableModel, string, object?>? propertyValueGetter)
	{
		return new BrowseItemComparer(
			settings.SortPropertyId,
			settings.SortDirection,
			propertyValueGetter);
	}

	private sealed class BrowseItemComparer : IComparer<IStorableModel>
	{
		private readonly string? propertyId;
		private readonly int direction;
		private readonly Func<IStorableModel, string, object?>? propertyValueGetter;

		public BrowseItemComparer(
			string? propertyId,
			ViewSortDirection sortDirection,
			Func<IStorableModel, string, object?>? propertyValueGetter)
		{
			this.propertyId = propertyId;
			direction = sortDirection is ViewSortDirection.Ascending ? 1 : -1;
			this.propertyValueGetter = propertyValueGetter;
		}

		public int Compare(IStorableModel? x, IStorableModel? y)
		{
			if (ReferenceEquals(x, y))
			{
				return 0;
			}

			if (x is null)
			{
				return -1;
			}

			if (y is null)
			{
				return 1;
			}

			var result = IsNameProperty(propertyId)
				? CompareNames(x, y)
				: ComparePropertyValues(x, y);
			if (result is not 0)
			{
				return direction * result;
			}

			result = CompareNames(x, y);
			if (result is not 0)
			{
				return result;
			}

			result = StringComparer.Ordinal.Compare(
				x.Reference.SourceId.Value,
				y.Reference.SourceId.Value);
			return result is not 0
				? result
				: StringComparer.Ordinal.Compare(
					x.Reference.ItemId,
					y.Reference.ItemId);
		}

		private int ComparePropertyValues(
			IStorableModel x,
			IStorableModel y)
		{
			var xValue = propertyValueGetter?.Invoke(x, propertyId!);
			var yValue = propertyValueGetter?.Invoke(y, propertyId!);
			if (xValue is null || yValue is null)
			{
				if (xValue is null && yValue is null)
				{
					return 0;
				}

				// Unavailable values stay at the end in both directions.
				return xValue is null ? direction : -direction;
			}

			if (xValue is string xText && yValue is string yText)
			{
				return StringComparer.OrdinalIgnoreCase.Compare(xText, yText);
			}

			if (IsNumber(xValue) && IsNumber(yValue))
			{
				try
				{
					return decimal.Compare(
						Convert.ToDecimal(xValue, CultureInfo.InvariantCulture),
						Convert.ToDecimal(yValue, CultureInfo.InvariantCulture));
				}
				catch (OverflowException)
				{
					// Fall through to the invariant string representation.
				}
			}

			if (xValue.GetType() == yValue.GetType()
				&& xValue is IComparable comparable)
			{
				try
				{
					return comparable.CompareTo(yValue);
				}
				catch (ArgumentException)
				{
					// Fall through to the invariant string representation.
				}
			}

			return StringComparer.OrdinalIgnoreCase.Compare(
				Convert.ToString(xValue, CultureInfo.InvariantCulture),
				Convert.ToString(yValue, CultureInfo.InvariantCulture));
		}

		private static int CompareNames(
			IStorableModel x,
			IStorableModel y)
		{
			return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
		}

		private static bool IsNameProperty(string? candidate)
		{
			return string.IsNullOrWhiteSpace(candidate)
				|| candidate.Equals("name", StringComparison.OrdinalIgnoreCase)
				|| candidate.Equals(ItemNamePropertyId, StringComparison.Ordinal);
		}

		private static bool IsNumber(object value)
		{
			return value is byte
				or sbyte
				or short
				or ushort
				or int
				or uint
				or long
				or ulong
				or float
				or double
				or decimal;
		}
	}
}

internal sealed class BrowseItemChangeSet
{
	public static BrowseItemChangeSet Empty { get; } = new([]);

	public BrowseItemChangeSet(IReadOnlyList<BrowseItemChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);
		Changes = changes;
	}

	public IReadOnlyList<BrowseItemChange> Changes { get; }

	public bool IsEmpty => Changes.Count is 0;
}

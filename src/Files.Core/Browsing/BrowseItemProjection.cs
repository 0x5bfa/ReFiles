// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;
using System.Globalization;

namespace Files.Core.Browsing;

internal sealed class BrowseItemProjection
{
	private const string _itemNamePropertyId = "System.ItemNameDisplay";

	private readonly Dictionary<StorableKey, IStorableModel> _modelsByKey = [];
	private readonly List<IStorableModel> _orderedItems = [];
	private readonly Func<IStorableModel, string, object?>? _propertyValueGetter;
	private IReadOnlyList<IStorableModel> _orderedItemsSnapshot = [];
	private IComparer<IStorableModel> _comparer;
	private bool _isSorted = true;

	public IReadOnlyList<IStorableModel> Items => Volatile.Read(ref _orderedItemsSnapshot);

	public BrowseItemProjection(BrowseViewSettings settings, Func<IStorableModel, string, object?>? propertyValueGetter = null)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_propertyValueGetter = propertyValueGetter;
		_comparer = CreateComparer(settings, propertyValueGetter);
	}

	public bool Contains(StorableKey key) => _modelsByKey.ContainsKey(key);

	public bool TryGet(StorableKey key, out IStorableModel model)
	{
		return _modelsByKey.TryGetValue(key, out model!);
	}

	public IReadOnlyList<IStorableModel> SortItems(IReadOnlyList<IStorableModel> models)
	{
		ArgumentNullException.ThrowIfNull(models);

		var sortedItems = models.ToArray();
		Array.Sort(sortedItems, _comparer);

		return sortedItems;
	}

	public bool TryGet(StorableKey key, out IStorableModel model, out int index)
	{
		if (!_modelsByKey.TryGetValue(key, out var foundModel))
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
		if (_modelsByKey.ContainsKey(key))
		{
			return BrowseItemChangeSet.Empty;
		}

		var index = FindInsertionIndex(model);
		_modelsByKey.Add(key, model);
		_orderedItems.Insert(index, model);
		UpdateSnapshot();

		return new BrowseItemChangeSet([ new BrowseItemAdded(index, model)]);
	}

	public BrowseItemChangeSet AddRange(IReadOnlyList<IStorableModel> models, bool preserveInputOrder = false)
	{
		ArgumentNullException.ThrowIfNull(models);

		if (models.Count is 0)
		{
			return BrowseItemChangeSet.Empty;
		}

		var incomingKeys = new HashSet<StorableKey>();
		foreach (var model in models)
		{
			ArgumentNullException.ThrowIfNull(model);

			var key = model.Reference.GetKey();
			if (!incomingKeys.Add(key) || _modelsByKey.ContainsKey(key))
			{
				throw new InvalidOperationException("The item projection contains duplicate keys.");
			}
		}

		if (preserveInputOrder)
		{
			var startingIndex = _orderedItems.Count;
			var addedItems = Array.AsReadOnly(models.ToArray());
			var previousItem = _orderedItems.LastOrDefault();
			foreach (var model in addedItems)
			{
				if (_isSorted && previousItem is not null && _comparer.Compare(previousItem, model) > 0)
				{
					_isSorted = false;
				}

				_modelsByKey.Add(model.Reference.GetKey(), model);
				_orderedItems.Add(model);
				previousItem = model;
			}

			UpdateSnapshot();

			return new BrowseItemChangeSet([new BrowseItemsAdded(startingIndex, addedItems)]);
		}

		var incomingItems = models.ToList();
		incomingItems.Sort(_comparer);
		var mergedItems = new List<IStorableModel>(_orderedItems.Count + incomingItems.Count);
		var changes = new List<BrowseItemChange>(incomingItems.Count);
		var existingIndex = 0;
		var incomingIndex = 0;
		while (existingIndex < _orderedItems.Count && incomingIndex < incomingItems.Count)
		{
			if (_comparer.Compare(_orderedItems[existingIndex], incomingItems[incomingIndex]) <= 0)
			{
				mergedItems.Add(_orderedItems[existingIndex++]);
				continue;
			}

			var incomingItem = incomingItems[incomingIndex++];
			changes.Add(new BrowseItemAdded(mergedItems.Count, incomingItem));
			mergedItems.Add(incomingItem);
		}

		while (existingIndex < _orderedItems.Count)
		{
			mergedItems.Add(_orderedItems[existingIndex++]);
		}

		while (incomingIndex < incomingItems.Count)
		{
			var incomingItem = incomingItems[incomingIndex++];
			changes.Add(new BrowseItemAdded(mergedItems.Count, incomingItem));
			mergedItems.Add(incomingItem);
		}

		foreach (var model in incomingItems)
		{
			_modelsByKey.Add(model.Reference.GetKey(), model);
		}

		_orderedItems.Clear();
		_orderedItems.AddRange(mergedItems);
		_isSorted = true;
		UpdateSnapshot();

		return new BrowseItemChangeSet(changes);
	}

	public BrowseItemChangeSet Sort()
	{
		if (_isSorted)
		{
			return BrowseItemChangeSet.Empty;
		}

		var previousKeys = _orderedItems.Select(static item => item.Reference.GetKey()).ToArray();
		_orderedItems.Sort(_comparer);
		_isSorted = true;
		if (previousKeys.SequenceEqual(_orderedItems.Select(static item => item.Reference.GetKey())))
		{
			return BrowseItemChangeSet.Empty;
		}

		UpdateSnapshot();

		return new BrowseItemChangeSet([ new BrowseItemsReset(Items)]);
	}

	public BrowseItemChangeSet Remove(StorableKey key)
	{
		if (!_modelsByKey.Remove(key, out _))
		{
			return BrowseItemChangeSet.Empty;
		}

		var index = FindItemIndex(key);
		if (index < 0)
		{
			throw new InvalidOperationException("The item projection is inconsistent.");
		}

		_orderedItems.RemoveAt(index);
		UpdateSnapshot();

		return new BrowseItemChangeSet([ new BrowseItemRemoved(index, key)]);
	}

	public BrowseItemChangeSet Replace(StorableKey previousKey, IStorableModel replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);

		if (!_modelsByKey.ContainsKey(previousKey))
		{
			throw new InvalidOperationException("The item to replace does not exist.");
		}

		var replacementKey = replacement.Reference.GetKey();
		if (replacementKey != previousKey && _modelsByKey.ContainsKey(replacementKey))
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
			_modelsByKey[previousKey] = replacement;
		}
		else
		{
			_modelsByKey.Remove(previousKey);
			_modelsByKey.Add(replacementKey, replacement);
		}

		_orderedItems[previousIndex] = replacement;
		_orderedItems.Sort(_comparer);
		_isSorted = true;
		var currentIndex = FindItemIndex(replacementKey);
		UpdateSnapshot();

		var changes = new List<BrowseItemChange>
		{
			new BrowseItemReplaced(previousIndex, previousKey, replacement),
		};

		if (previousIndex != currentIndex)
		{
			changes.Add(new BrowseItemMoved(previousIndex, currentIndex, replacementKey));
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

		_orderedItems.Clear();
		_orderedItems.AddRange(nextModels);
		_orderedItems.Sort(_comparer);
		_isSorted = true;
		_modelsByKey.Clear();
		foreach (var pair in nextByKey)
		{
			_modelsByKey.Add(pair.Key, pair.Value);
		}

		UpdateSnapshot();

		return new BrowseItemChangeSet([ new BrowseItemsReset(Items)]);
	}

	public BrowseItemChangeSet UpdateSort(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_comparer = CreateComparer(settings, _propertyValueGetter);
		_isSorted = _orderedItems.Count < 2;

		return Sort();
	}

	private int FindInsertionIndex(IStorableModel model)
	{
		var low = 0;
		var high = _orderedItems.Count;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (_comparer.Compare(_orderedItems[middle], model) <= 0)
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
		for (var index = 0; index < _orderedItems.Count; index++)
		{
			if (_orderedItems[index].Reference.GetKey() == key)
			{
				return index;
			}
		}

		return -1;
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _orderedItemsSnapshot, Array.AsReadOnly(_orderedItems.ToArray()));
	}

	private static IComparer<IStorableModel> CreateComparer(BrowseViewSettings settings, Func<IStorableModel, string, object?>? propertyValueGetter)
	{
		return new BrowseItemComparer(settings.SortPropertyId, settings.SortDirection, propertyValueGetter);
	}

	private sealed class BrowseItemComparer : IComparer<IStorableModel>
	{
		private readonly string? _propertyId;
		private readonly int _direction;
		private readonly Func<IStorableModel, string, object?>? _propertyValueGetter;

		public BrowseItemComparer(string? propertyId, ViewSortDirection sortDirection, Func<IStorableModel, string, object?>? propertyValueGetter)
		{
			_propertyId = propertyId;
			_direction = sortDirection is ViewSortDirection.Ascending ? 1 : -1;
			_propertyValueGetter = propertyValueGetter;
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

			var result = IsNameProperty(_propertyId) ? CompareNames(x, y) : ComparePropertyValues(x, y);
			if (result is not 0)
			{
				return _direction * result;
			}

			result = CompareNames(x, y);
			if (result is not 0)
			{
				return result;
			}

			result = StringComparer.Ordinal.Compare(x.Reference.SourceId.Value, y.Reference.SourceId.Value);

			return result is not 0
				? result
				: StringComparer.Ordinal.Compare(x.Reference.ItemId, y.Reference.ItemId);
		}

		private int ComparePropertyValues(IStorableModel x, IStorableModel y)
		{
			var xValue = _propertyValueGetter?.Invoke(x, _propertyId!);
			var yValue = _propertyValueGetter?.Invoke(y, _propertyId!);
			if (xValue is null || yValue is null)
			{
				if (xValue is null && yValue is null)
				{
					return 0;
				}

				// Unavailable values stay at the end in both directions.

				return xValue is null ? _direction : -_direction;
			}

			if (xValue is string xText && yValue is string yText)
			{
				return StringComparer.OrdinalIgnoreCase.Compare(xText, yText);
			}

			if (IsNumber(xValue) && IsNumber(yValue))
			{
				try
				{
					return decimal.Compare(Convert.ToDecimal(xValue, CultureInfo.InvariantCulture), Convert.ToDecimal(yValue, CultureInfo.InvariantCulture));
				}
				catch (OverflowException)
				{
					// Fall through to the invariant string representation.
				}
			}

			if (xValue.GetType() == yValue.GetType() && xValue is IComparable comparable)
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

			return StringComparer.OrdinalIgnoreCase.Compare(Convert.ToString(xValue, CultureInfo.InvariantCulture), Convert.ToString(yValue, CultureInfo.InvariantCulture));
		}

		private static int CompareNames(IStorableModel x, IStorableModel y)
		{
			return StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
		}

		private static bool IsNameProperty(string? candidate)
		{
			return string.IsNullOrWhiteSpace(candidate) ||
				candidate.Equals("name", StringComparison.OrdinalIgnoreCase) ||
				candidate.Equals(_itemNamePropertyId, StringComparison.Ordinal);
		}

		private static bool IsNumber(object value)
		{
			return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
		}
	}
}

internal sealed class BrowseItemChangeSet
{
	public static BrowseItemChangeSet Empty { get; } = new([]);

	public IReadOnlyList<BrowseItemChange> Changes { get; }

	public bool IsEmpty => Changes.Count is 0;

	public BrowseItemChangeSet(IReadOnlyList<BrowseItemChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		Changes = changes;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;
using System.Globalization;

namespace Files.Core.Browsing;

internal sealed class BrowseItemProjection
{
	private const string ItemNamePropertyId = "System.ItemNameDisplay";

	private readonly Lock _syncRoot = new();
	private readonly Dictionary<StorableKey, IStorableModel> _modelsByKey = [];
	private readonly Dictionary<StorableKey, int> _indicesByKey = [];
	private readonly List<IStorableModel> _orderedItems = [];
	private readonly Func<IStorableModel, string, object?>? _propertyValueGetter;
	private IReadOnlyList<IStorableModel> _orderedItemsSnapshot = [];
	private IComparer<IStorableModel> _comparer;
	private string? _sortPropertyId;
	private ViewSortDirection _sortDirection;
	private bool _isExternallySorted;
	private bool _isSorted = true;
	private bool _snapshotDirty;

	public IReadOnlyList<IStorableModel> Items
	{
		get
		{
			lock (_syncRoot)
			{
				return GetSnapshotLocked();
			}
		}
	}

	public BrowseItemProjection(BrowseViewSettings settings, Func<IStorableModel, string, object?>? propertyValueGetter = null)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_propertyValueGetter = propertyValueGetter;
		_sortPropertyId = settings.SortPropertyId;
		_sortDirection = settings.SortDirection;
		_comparer = CreateComparer(settings, propertyValueGetter);
	}

	public bool Contains(StorableKey key)
	{
		lock (_syncRoot)
		{
			return _modelsByKey.ContainsKey(key);
		}
	}

	public bool TryGet(StorableKey key, out IStorableModel model)
	{
		lock (_syncRoot)
		{
			return _modelsByKey.TryGetValue(key, out model!);
		}
	}

	internal bool TryApplyToCurrent(StorableKey key, IStorableModel expectedModel, Func<bool> apply)
	{
		ArgumentNullException.ThrowIfNull(expectedModel);
		ArgumentNullException.ThrowIfNull(apply);

		lock (_syncRoot)
		{
			return _modelsByKey.TryGetValue(key, out var currentModel) && ReferenceEquals(currentModel, expectedModel) && apply();
		}
	}

	internal void InvokeLocked(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		lock (_syncRoot)
		{
			action();
		}
	}

	public IReadOnlyList<IStorableModel> SortItems(IReadOnlyList<IStorableModel> models)
	{
		ArgumentNullException.ThrowIfNull(models);

		lock (_syncRoot)
		{
			var sortedItems = models.ToArray();
			Array.Sort(sortedItems, _comparer);

			return sortedItems;
		}
	}

	public bool TryGet(StorableKey key, out IStorableModel model, out int index)
	{
		lock (_syncRoot)
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
	}

	public BrowseItemChangeSet Add(IStorableModel model)
	{
		ArgumentNullException.ThrowIfNull(model);

		lock (_syncRoot)
		{
			var key = model.Reference.GetKey();
			if (_modelsByKey.ContainsKey(key))
			{
				return BrowseItemChangeSet.Empty;
			}

			var index = _isExternallySorted ? _orderedItems.Count : FindInsertionIndex(model);
			_modelsByKey.Add(key, model);
			_orderedItems.Insert(index, model);
			_isSorted = !_isExternallySorted;
			RebuildIndices();
			UpdateSnapshot();

			return new BrowseItemChangeSet([ new BrowseItemAdded(index, model)]);
		}
	}

	public BrowseItemChangeSet AddRange(IReadOnlyList<IStorableModel> models, bool preserveInputOrder = false)
	{
		ArgumentNullException.ThrowIfNull(models);

		if (models.Count is 0)
		{
			return BrowseItemChangeSet.Empty;
		}

		lock (_syncRoot)
		{
			if (_isExternallySorted)
			{
				preserveInputOrder = true;
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

				if (_isExternallySorted)
				{
					_isSorted = false;
				}

				RebuildIndices();
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
			RebuildIndices();
			UpdateSnapshot();

			return new BrowseItemChangeSet(changes);
		}
	}

	public BrowseItemChangeSet Sort()
	{
		lock (_syncRoot)
		{
			if (_isExternallySorted)
			{
				_isExternallySorted = false;
				_isSorted = _orderedItems.Count < 2;
			}

			return SortCore();
		}
	}

	public BrowseItemChangeSet ApplyExternalOrder(IReadOnlyList<IStorableModel> models)
	{
		ArgumentNullException.ThrowIfNull(models);

		lock (_syncRoot)
		{
			var orderedKeys = new HashSet<StorableKey>();
			var isValidOrder = models.Count == _orderedItems.Count;
			foreach (var model in models)
			{
				var key = model.Reference.GetKey();
				isValidOrder &= orderedKeys.Add(key) && _modelsByKey.TryGetValue(key, out var current) && ReferenceEquals(current, model);
			}

			if (!isValidOrder)
			{
				throw new InvalidOperationException("The external order must contain every projected item exactly once.");
			}

			var previousKeys = _orderedItems.Select(static item => item.Reference.GetKey()).ToArray();
			_orderedItems.Clear();
			_orderedItems.AddRange(models);
			_isExternallySorted = true;
			_isSorted = true;
			RebuildIndices();
			if (previousKeys.SequenceEqual(_orderedItems.Select(static item => item.Reference.GetKey())))
			{
				return BrowseItemChangeSet.Empty;
			}

			UpdateSnapshot();

			return new BrowseItemChangeSet([new BrowseItemsReset(GetSnapshotLocked())]);
		}
	}

	public BrowseItemChangeSet Remove(StorableKey key)
	{
		lock (_syncRoot)
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
			RebuildIndices();
			UpdateSnapshot();

			return new BrowseItemChangeSet([new BrowseItemRemoved(index, key)]);
		}
	}

	public BrowseItemChangeSet Replace(StorableKey previousKey, IStorableModel replacement)
	{
		ArgumentNullException.ThrowIfNull(replacement);

		lock (_syncRoot)
		{
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
			if (!_isExternallySorted)
			{
				_orderedItems.Sort(_comparer);
				_isSorted = true;
			}
			else
			{
				_isSorted = false;
			}
			RebuildIndices();
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
	}

	public BrowseItemChangeSet Reset(IEnumerable<IStorableModel> models)
	{
		ArgumentNullException.ThrowIfNull(models);

		lock (_syncRoot)
		{
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
			_isExternallySorted = false;
			_isSorted = true;
			_modelsByKey.Clear();
			foreach (var pair in nextByKey)
			{
				_modelsByKey.Add(pair.Key, pair.Value);
			}

			RebuildIndices();
			UpdateSnapshot();

			return new BrowseItemChangeSet([new BrowseItemsReset(GetSnapshotLocked())]);
		}
	}

	public BrowseItemChangeSet UpdateSort(BrowseViewSettings settings, bool deferSort = false)
	{
		ArgumentNullException.ThrowIfNull(settings);

		lock (_syncRoot)
		{
			if (string.Equals(_sortPropertyId, settings.SortPropertyId, StringComparison.Ordinal) && _sortDirection == settings.SortDirection)
			{
				return BrowseItemChangeSet.Empty;
			}

			_sortPropertyId = settings.SortPropertyId;
			_sortDirection = settings.SortDirection;
			_comparer = CreateComparer(settings, _propertyValueGetter);
			_isExternallySorted = false;
			_isSorted = _orderedItems.Count < 2;
			if (deferSort)
			{
				return BrowseItemChangeSet.Empty;
			}

			return SortCore();
		}
	}

	public BrowseItemChangeSet RefreshSort()
	{
		lock (_syncRoot)
		{
			_isExternallySorted = false;
			_isSorted = _orderedItems.Count < 2;

			return SortCore();
		}
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
		return _indicesByKey.GetValueOrDefault(key, -1);
	}

	private void UpdateSnapshot()
	{
		_snapshotDirty = true;
	}

	private IReadOnlyList<IStorableModel> GetSnapshotLocked()
	{
		if (_snapshotDirty)
		{
			Volatile.Write(ref _orderedItemsSnapshot, Array.AsReadOnly(_orderedItems.ToArray()));
			_snapshotDirty = false;
		}

		return _orderedItemsSnapshot;
	}

	private void RebuildIndices()
	{
		_indicesByKey.Clear();
		for (var index = 0; index < _orderedItems.Count; index++)
		{
			_indicesByKey[_orderedItems[index].Reference.GetKey()] = index;
		}
	}

	private BrowseItemChangeSet SortCore()
	{
		if (_isSorted)
		{
			return BrowseItemChangeSet.Empty;
		}

		var previousKeys = _orderedItems.Select(static item => item.Reference.GetKey()).ToArray();
		_orderedItems.Sort(_comparer);
		_isSorted = true;
		RebuildIndices();
		if (previousKeys.SequenceEqual(_orderedItems.Select(static item => item.Reference.GetKey())))
		{
			return BrowseItemChangeSet.Empty;
		}

		UpdateSnapshot();

		return new BrowseItemChangeSet([new BrowseItemsReset(GetSnapshotLocked())]);
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

			var xIsFolder = x is IFolderModel;
			var yIsFolder = y is IFolderModel;
			if (xIsFolder != yIsFolder)
			{
				return xIsFolder ? -1 : 1;
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
				candidate.Equals(ItemNamePropertyId, StringComparison.Ordinal);
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

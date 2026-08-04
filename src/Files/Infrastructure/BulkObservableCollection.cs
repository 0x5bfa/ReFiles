// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace Files.Infrastructure;

/// <summary>
/// Provides an observable collection that can replace its contents with a single reset notification.
/// </summary>
[DebuggerDisplay("Count = {Count}")]
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
	private static readonly NotifyCollectionChangedEventArgs _resetEventArgs = new(NotifyCollectionChangedAction.Reset);
	private static readonly PropertyChangedEventArgs _countPropertyChangedEventArgs = new(nameof(Count));
	private static readonly PropertyChangedEventArgs _indexerPropertyChangedEventArgs = new("Item[]");

	/// <summary>
	/// Replaces all items and raises a single reset notification.
	/// </summary>
	/// <param name="items">The replacement items.</param>
	public void ReplaceAll(IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		var replacementItems = items.ToArray();
		CheckReentrancy();

		var countChanged = Count != replacementItems.Length;
		Items.Clear();
		foreach (var item in replacementItems)
		{
			Items.Add(item);
		}

		if (countChanged)
		{
			OnPropertyChanged(_countPropertyChangedEventArgs);
		}

		OnPropertyChanged(_indexerPropertyChangedEventArgs);
		OnCollectionChanged(_resetEventArgs);
	}

	/// <summary>
	/// Appends a range of items and raises one collection change notification.
	/// </summary>
	/// <param name="items">The items to append.</param>
	public void AddRange(IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		var addedItems = items.ToList();
		if (addedItems.Count is 0)
		{
			return;
		}

		CheckReentrancy();
		var startingIndex = Count;
		foreach (var item in addedItems)
		{
			Items.Add(item);
		}

		OnPropertyChanged(_countPropertyChangedEventArgs);
		OnPropertyChanged(_indexerPropertyChangedEventArgs);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)addedItems, startingIndex));
	}

	/// <summary>
	/// Inserts a range of items and raises one collection change notification.
	/// </summary>
	/// <param name="index">The insertion index.</param>
	/// <param name="items">The items to insert.</param>
	public void InsertRange(int index, IEnumerable<T> items)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);

		ArgumentNullException.ThrowIfNull(items);

		var insertedItems = items.ToList();
		if (insertedItems.Count is 0)
		{
			return;
		}

		CheckReentrancy();
		for (var offset = 0; offset < insertedItems.Count; offset++)
		{
			Items.Insert(index + offset, insertedItems[offset]);
		}

		OnPropertyChanged(_countPropertyChangedEventArgs);
		OnPropertyChanged(_indexerPropertyChangedEventArgs);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)insertedItems, index));
	}
}

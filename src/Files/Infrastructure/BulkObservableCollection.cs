// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

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
}

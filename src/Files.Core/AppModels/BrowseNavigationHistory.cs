// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Immutable representation of a pane's navigation history.
/// </summary>
public sealed record BrowseNavigationHistorySnapshot
{
	public BrowseNavigationHistorySnapshot(IEnumerable<BrowseLocation> entries, int currentIndex)
	{
		ArgumentNullException.ThrowIfNull(entries);

		var entryArray = entries.ToArray();
		if (entryArray.Any(static entry => entry is null))
		{
			throw new ArgumentException("History entries cannot contain null values.", nameof(entries));
		}

		if (entryArray.Length is 0)
		{
			if (currentIndex is not -1)
			{
				throw new ArgumentOutOfRangeException(nameof(currentIndex));
			}
		}
		else if (currentIndex < 0 || currentIndex >= entryArray.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(currentIndex));
		}

		Entries = Array.AsReadOnly(entryArray);
		CurrentIndex = currentIndex;
	}

	public IReadOnlyList<BrowseLocation> Entries { get; }

	public int CurrentIndex { get; }

	public BrowseLocation? Current =>
		CurrentIndex < 0 ? null : Entries[CurrentIndex];
}

/// <summary>
/// Tracks the committed locations of one pane.
/// </summary>
public sealed class BrowseNavigationHistory
{
	private const int DefaultCapacity = 50;

	private readonly object syncRoot = new();
	private readonly int capacity;
	private readonly List<BrowseLocation> entries = [];
	private IReadOnlyList<BrowseLocation> snapshot =
		Array.Empty<BrowseLocation>();
	private int currentIndex = -1;

	public BrowseNavigationHistory(int capacity = DefaultCapacity)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		this.capacity = capacity;
	}

	public IReadOnlyList<BrowseLocation> Entries =>
		Volatile.Read(ref snapshot);

	public int CurrentIndex
	{
		get
		{
			lock (syncRoot)
			{
				return currentIndex;
			}
		}
	}

	public BrowseLocation? Current
	{
		get
		{
			lock (syncRoot)
			{
				return currentIndex < 0 ? null : entries[currentIndex];
			}
		}
	}

	public bool CanGoBack
	{
		get
		{
			lock (syncRoot)
			{
				return currentIndex > 0;
			}
		}
	}

	public bool CanGoForward
	{
		get
		{
			lock (syncRoot)
			{
				return currentIndex >= 0 && currentIndex < entries.Count - 1;
			}
		}
	}

	public event EventHandler? Changed;

	public BrowseNavigationHistorySnapshot Capture()
	{
		lock (syncRoot)
		{
			return new BrowseNavigationHistorySnapshot(entries, currentIndex);
		}
	}

	internal void Push(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);
		var changed = false;

		lock (syncRoot)
		{
			if (currentIndex >= 0 && Equals(entries[currentIndex], location))
			{
				if (ReferenceEquals(entries[currentIndex], location))
				{
					return;
				}

				entries[currentIndex] = location;
				UpdateSnapshot();
				changed = true;
			}
			else
			{
				if (currentIndex < entries.Count - 1)
				{
					entries.RemoveRange(currentIndex + 1, entries.Count - currentIndex - 1);
				}

				entries.Add(location);
				currentIndex = entries.Count - 1;

				if (entries.Count > capacity)
				{
					var removeCount = entries.Count - capacity;
					entries.RemoveRange(0, removeCount);
					currentIndex -= removeCount;
				}

				UpdateSnapshot();
				changed = true;
			}
		}

		if (changed)
		{
			ModelEvent.Raise(this, Changed);
		}
	}

	internal void Replace(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);
		var changed = false;

		lock (syncRoot)
		{
			if (currentIndex < 0)
			{
				entries.Add(location);
				currentIndex = 0;
				changed = true;
			}
			else if (!ReferenceEquals(entries[currentIndex], location))
			{
				entries[currentIndex] = location;
				changed = true;
			}

			if (changed)
			{
				UpdateSnapshot();
			}
		}

		if (changed)
		{
			ModelEvent.Raise(this, Changed);
		}
	}

	internal bool TryGetBack(out BrowseLocation? location, out int targetIndex)
	{
		lock (syncRoot)
		{
			targetIndex = currentIndex - 1;
			location = targetIndex >= 0 ? entries[targetIndex] : null;
			return location is not null;
		}
	}

	internal bool TryGetForward(out BrowseLocation? location, out int targetIndex)
	{
		lock (syncRoot)
		{
			targetIndex = currentIndex + 1;
			location = targetIndex < entries.Count ? entries[targetIndex] : null;
			return location is not null;
		}
	}

	internal bool TryMoveTo(int targetIndex, BrowseLocation expectedLocation)
	{
		ArgumentNullException.ThrowIfNull(expectedLocation);
		var changed = false;

		lock (syncRoot)
		{
			if (targetIndex < 0
				|| targetIndex >= entries.Count
				|| !Equals(entries[targetIndex], expectedLocation))
			{
				return false;
			}

			if (currentIndex != targetIndex)
			{
				currentIndex = targetIndex;
				changed = true;
			}
		}

		if (changed)
		{
			ModelEvent.Raise(this, Changed);
		}

		return true;
	}

	internal void Restore(BrowseNavigationHistorySnapshot restored)
	{
		ArgumentNullException.ThrowIfNull(restored);

		lock (syncRoot)
		{
			entries.Clear();

			var sourceEntries = restored.Entries;
			var firstIndex = sourceEntries.Count <= capacity
				? 0
				: Math.Clamp(restored.CurrentIndex - (capacity / 2), 0, sourceEntries.Count - capacity);
			var lastIndex = Math.Min(sourceEntries.Count, firstIndex + capacity);
			for (var index = firstIndex; index < lastIndex; index++)
			{
				entries.Add(sourceEntries[index]);
			}

			currentIndex = restored.CurrentIndex < 0
				? -1
				: restored.CurrentIndex - firstIndex;
			UpdateSnapshot();
		}

		ModelEvent.Raise(this, Changed);
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref snapshot, Array.AsReadOnly(entries.ToArray()));
	}
}

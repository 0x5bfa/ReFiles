// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.AppModels;

/// <summary>
/// Immutable representation of a pane's navigation history.
/// </summary>
public sealed record BrowseNavigationHistorySnapshot
{
	public IReadOnlyList<BrowseLocation> Entries { get; }

	public int CurrentIndex { get; }

	public BrowseLocation? Current =>
		CurrentIndex < 0 ? null : Entries[CurrentIndex];

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
}

/// <summary>
/// Tracks the committed locations of one pane.
/// </summary>
public sealed class BrowseNavigationHistory
{
	private const int _defaultCapacity = 50;

	private readonly Lock _syncRoot = new();

	private readonly int _capacity;

	private readonly List<BrowseLocation> _entries = [];

	private IReadOnlyList<BrowseLocation> _snapshot = [];

	private int _currentIndex = -1;

	public IReadOnlyList<BrowseLocation> Entries => Volatile.Read(ref _snapshot);

	public int CurrentIndex
	{
		get
		{
			lock (_syncRoot)
			{
				return _currentIndex;
			}
		}
	}

	public BrowseLocation? Current
	{
		get
		{
			lock (_syncRoot)
			{
				return _currentIndex < 0 ? null : _entries[_currentIndex];
			}
		}
	}

	public bool CanGoBack
	{
		get
		{
			lock (_syncRoot)
			{
				return _currentIndex > 0;
			}
		}
	}

	public bool CanGoForward
	{
		get
		{
			lock (_syncRoot)
			{
				return _currentIndex >= 0 && _currentIndex < _entries.Count - 1;
			}
		}
	}

	public event EventHandler? Changed;

	public BrowseNavigationHistory(int capacity = _defaultCapacity)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

		_capacity = capacity;
	}

	public BrowseNavigationHistorySnapshot Capture()
	{
		lock (_syncRoot)
		{
			return new BrowseNavigationHistorySnapshot(_entries, _currentIndex);
		}
	}

	internal void Push(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		var changed = false;

		lock (_syncRoot)
		{
			if (_currentIndex >= 0 && Equals(_entries[_currentIndex], location))
			{
				if (ReferenceEquals(_entries[_currentIndex], location))
				{
					return;
				}

				_entries[_currentIndex] = location;
				UpdateSnapshot();
				changed = true;
			}
			else
			{
				if (_currentIndex < _entries.Count - 1)
				{
					_entries.RemoveRange(_currentIndex + 1, _entries.Count - _currentIndex - 1);
				}

				_entries.Add(location);
				_currentIndex = _entries.Count - 1;

				if (_entries.Count > _capacity)
				{
					var removeCount = _entries.Count - _capacity;
					_entries.RemoveRange(0, removeCount);
					_currentIndex -= removeCount;
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

		lock (_syncRoot)
		{
			if (_currentIndex < 0)
			{
				_entries.Add(location);
				_currentIndex = 0;
				changed = true;
			}
			else if (!ReferenceEquals(_entries[_currentIndex], location))
			{
				_entries[_currentIndex] = location;
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
		lock (_syncRoot)
		{
			targetIndex = _currentIndex - 1;
			location = targetIndex >= 0 ? _entries[targetIndex] : null;

			return location is not null;
		}
	}

	internal bool TryGetForward(out BrowseLocation? location, out int targetIndex)
	{
		lock (_syncRoot)
		{
			targetIndex = _currentIndex + 1;
			location = targetIndex < _entries.Count ? _entries[targetIndex] : null;

			return location is not null;
		}
	}

	internal bool TryMoveTo(int targetIndex, BrowseLocation expectedLocation)
	{
		ArgumentNullException.ThrowIfNull(expectedLocation);

		var changed = false;

		lock (_syncRoot)
		{
			if (targetIndex < 0 || targetIndex >= _entries.Count || !Equals(_entries[targetIndex], expectedLocation))
			{
				return false;
			}

			if (_currentIndex != targetIndex)
			{
				_currentIndex = targetIndex;
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

		lock (_syncRoot)
		{
			_entries.Clear();

			var sourceEntries = restored.Entries;
			var firstIndex = sourceEntries.Count <= _capacity ? 0 : Math.Clamp(restored.CurrentIndex - (_capacity / 2), 0, sourceEntries.Count - _capacity);
			var lastIndex = Math.Min(sourceEntries.Count, firstIndex + _capacity);
			for (var index = firstIndex; index < lastIndex; index++)
			{
				_entries.Add(sourceEntries[index]);
			}

			_currentIndex = restored.CurrentIndex < 0 ? -1 : restored.CurrentIndex - firstIndex;
			UpdateSnapshot();
		}

		ModelEvent.Raise(this, Changed);
	}

	private void UpdateSnapshot()
	{
		Volatile.Write(ref _snapshot, Array.AsReadOnly(_entries.ToArray()));
	}
}

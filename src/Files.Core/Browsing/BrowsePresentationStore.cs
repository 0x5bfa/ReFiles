// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Properties;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Models;

namespace Files.Core.Browsing;

internal sealed class BrowsePresentationStore
{
	private readonly Lock _lock = new();
	private readonly Dictionary<StorableKey, Entry> _entries = [];
	private long _version;

	internal bool TryGet(StorableKey key, out BrowseItemPresentation presentation)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var entry))
			{
				presentation = entry.Presentation;

				return true;
			}
		}

		presentation = null!;

		return false;
	}

	internal BrowseItemPresentation UpdateProperties(long generation, StorableKey key, IStorableModel item, IReadOnlyDictionary<string, object?> properties)
	{
		lock (_lock)
		{
			var current = _entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Item, item) ? entry.Presentation : new BrowseItemPresentation();
			var mergedProperties = new Dictionary<string, object?>(current.Properties, StringComparer.Ordinal);
			foreach (var pair in properties)
			{
				mergedProperties[pair.Key] = pair.Value;
			}

			var next = new BrowseItemPresentation(mergedProperties, current.Thumbnail, generation, checked(++_version));
			_entries[key] = new Entry(item, next);

			return next;
		}
	}

	internal bool TryUpdateThumbnail(long generation, StorableKey key, IStorableModel item, ThumbnailResult thumbnail, out BrowseItemPresentation presentation)
	{
		lock (_lock)
		{
			var current = _entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Item, item) ? entry.Presentation : new BrowseItemPresentation();
			if (AreEquivalent(current.Thumbnail, thumbnail))
			{
				presentation = current;

				return false;
			}

			presentation = new BrowseItemPresentation(current.Properties, thumbnail, generation, checked(++_version));
			_entries[key] = new Entry(item, presentation);

			return true;
		}
	}

	internal object? GetSortPropertyValue(IStorableModel item, string propertyId)
	{
		lock (_lock)
		{
			var key = item.Reference.GetKey();

			if (!_entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Item, item) || !entry.Presentation.Properties.TryGetValue(propertyId, out var value))
			{
				return null;
			}

			return value is FormattedPropertyValue formattedValue ? formattedValue.RawValue : value;
		}
	}

	internal void Clear()
	{
		lock (_lock)
		{
			_entries.Clear();
		}
	}

	internal State CaptureState()
	{
		lock (_lock)
		{
			return new State(new Dictionary<StorableKey, Entry>(_entries));
		}
	}

	internal void RestoreState(State state)
	{
		ArgumentNullException.ThrowIfNull(state);

		lock (_lock)
		{
			_entries.Clear();
			foreach (var pair in state.Entries)
			{
				_entries.Add(pair.Key, pair.Value);
			}
		}
	}

	internal BrowseItemPresentationChangedEventArgs[] ClearThumbnails()
	{
		var changes = new List<BrowseItemPresentationChangedEventArgs>();
		lock (_lock)
		{
			foreach (var pair in _entries.ToArray())
			{
				if (pair.Value.Presentation.Thumbnail is null)
				{
					continue;
				}

				var presentation = new BrowseItemPresentation(pair.Value.Presentation.Properties, generation: pair.Value.Presentation.Generation, version: checked(++_version));
				_entries[pair.Key] = new Entry(pair.Value.Item, presentation);
				changes.Add(new BrowseItemPresentationChangedEventArgs(pair.Key, presentation, BrowseItemPresentationChangeFlags.Thumbnail));
			}
		}

		return [.. changes];
	}

	internal void Remove(StorableKey key)
	{
		lock (_lock)
		{
			_entries.Remove(key);
		}
	}

	private static bool AreEquivalent(ThumbnailResult? current, ThumbnailResult candidate)
	{
		if (ReferenceEquals(current, candidate))
		{
			return true;
		}

		if (current is null)
		{
			return false;
		}

		return current.IsFallback == candidate.IsFallback
			&& current.Format == candidate.Format
			&& current.PixelWidth == candidate.PixelWidth
			&& current.PixelHeight == candidate.PixelHeight
			&& string.Equals(current.ContentType, candidate.ContentType, StringComparison.Ordinal)
			&& current.Content.Span.SequenceEqual(candidate.Content.Span);
	}

	internal sealed class State
	{
		private readonly Dictionary<StorableKey, Entry> _entries;

		internal Dictionary<StorableKey, Entry> Entries => _entries;

		internal State(Dictionary<StorableKey, Entry> entries)
		{
			_entries = entries;
		}
	}

	internal sealed record Entry(IStorableModel Item, BrowseItemPresentation Presentation);
}

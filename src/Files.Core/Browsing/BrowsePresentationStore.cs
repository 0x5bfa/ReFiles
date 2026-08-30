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

	internal BrowseItemPresentation UpdateProperties(StorableKey key, IStorableModel item, IReadOnlyDictionary<string, object?> properties)
	{
		lock (_lock)
		{
			var current = _entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Item, item) ? entry.Presentation : new BrowseItemPresentation();
			var mergedProperties = new Dictionary<string, object?>(current.Properties, StringComparer.Ordinal);
			foreach (var pair in properties)
			{
				mergedProperties[pair.Key] = pair.Value;
			}

			var next = new BrowseItemPresentation(mergedProperties, current.Thumbnail);
			_entries[key] = new Entry(item, next);

			return next;
		}
	}

	internal bool TryUpdateThumbnail(StorableKey key, IStorableModel item, ThumbnailResult thumbnail, out BrowseItemPresentation presentation)
	{
		lock (_lock)
		{
			var current = _entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Item, item) ? entry.Presentation : new BrowseItemPresentation();
			if (AreEquivalent(current.Thumbnail, thumbnail))
			{
				presentation = current;

				return false;
			}

			presentation = new BrowseItemPresentation(current.Properties, thumbnail);
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

	internal Snapshot Capture()
	{
		lock (_lock)
		{
			return new Snapshot(new Dictionary<StorableKey, Entry>(_entries));
		}
	}

	internal void Restore(Snapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		lock (_lock)
		{
			_entries.Clear();
			foreach (var pair in snapshot.Entries)
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

				var presentation = new BrowseItemPresentation(pair.Value.Presentation.Properties);
				_entries[pair.Key] = new Entry(pair.Value.Item, presentation);
				changes.Add(new BrowseItemPresentationChangedEventArgs(pair.Key, presentation, BrowseItemPresentationChangeFlags.Thumbnail));
			}
		}

		return changes.ToArray();
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

	internal sealed class Snapshot
	{
		private readonly Dictionary<StorableKey, Entry> _entries;

		internal Dictionary<StorableKey, Entry> Entries => _entries;

		internal Snapshot(Dictionary<StorableKey, Entry> entries)
		{
			_entries = entries;
		}
	}

	internal sealed record Entry(IStorableModel Item, BrowseItemPresentation Presentation);
}

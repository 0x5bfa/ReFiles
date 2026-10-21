// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Windows;

namespace Files.BackgroundServer;

internal sealed class ShellNewMenuCache
{
	private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);
	private readonly Lock _syncRoot = new();
	private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

	internal async Task<IReadOnlyList<ShellNewItemData>> GetItemsAsync(string folderPath, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

		var normalizedPath = Path.GetFullPath(folderPath);
		var now = DateTimeOffset.UtcNow;
		Task<IReadOnlyList<ShellNewItemData>> loadTask;
		lock (_syncRoot)
		{
			foreach (var expiredPath in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
			{
				_entries.Remove(expiredPath);
			}

			if (_entries.TryGetValue(normalizedPath, out var entry) && entry.ExpiresAt > now)
			{
				loadTask = entry.LoadTask;
			}
			else
			{
				loadTask = LoadAsync(normalizedPath);
				_entries[normalizedPath] = new CacheEntry(loadTask, now + _lifetime);
			}
		}

		try
		{
			return await loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch when (!cancellationToken.IsCancellationRequested)
		{
			lock (_syncRoot)
			{
				if (_entries.TryGetValue(normalizedPath, out var entry) && ReferenceEquals(entry.LoadTask, loadTask))
				{
					_entries.Remove(normalizedPath);
				}
			}

			throw;
		}
	}

	private static async Task<IReadOnlyList<ShellNewItemData>> LoadAsync(string folderPath)
	{
		var menu = new WindowsShellNewMenu(Program.Storage.Value.Source.Scheduler);
		var items = await menu.GetItemsAsync(folderPath).ConfigureAwait(false);

		return items.Select(static item => new ShellNewItemData
		{
			CommandOffset = item.CommandOffset,
			Name = item.Name,
			IconDataBase64 = item.IconData.IsEmpty ? string.Empty : Convert.ToBase64String(item.IconData.Span),
			IsEnabled = item.IsEnabled,
		}).ToArray();
	}

	private sealed record CacheEntry(Task<IReadOnlyList<ShellNewItemData>> LoadTask, DateTimeOffset ExpiresAt);
}

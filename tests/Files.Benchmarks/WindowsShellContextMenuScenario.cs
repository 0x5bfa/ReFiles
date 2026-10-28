// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Files.Core.Storage;
using Files.Core.Storage.Windows;

namespace Files.Benchmarks;

internal static class WindowsShellContextMenuScenario
{
	internal static async Task RunAsync(IEnumerable<string> paths)
	{
		await using var source = new WindowsStorageSource();
		foreach (var requestedPath in paths)
		{
			var path = Path.GetFullPath(requestedPath);
			var address = new StorageAddress(WindowsStorageSource.FileAddressScheme, path);
			var item = await source.ResolveAsync(address);
			StorableReference[] selection = [new(source.SourceId, item.Id, address)];
			for (var iteration = 0; iteration < 3; iteration++)
			{
				var clock = Stopwatch.StartNew();
				await using var session = await WindowsShellContextMenuSession.CreateAsync(source, selection);
				var constructionMilliseconds = clock.Elapsed.TotalMilliseconds;
				if (session is null)
				{
					Console.WriteLine(JsonSerializer.Serialize(new { Path = path, Error = "Selection has no native menu" }));
					continue;
				}

				var entries = Flatten(session.Entries).Where(static entry => entry.HasIcon).ToArray();
				var icons = await Task.WhenAll(entries.Select(entry => session.GetIconAsync(entry, 24)));
				Console.WriteLine(JsonSerializer.Serialize(new
				{
					Path = path, Iteration = iteration, ConstructionMilliseconds = constructionMilliseconds, WithIconsMilliseconds = clock.Elapsed.TotalMilliseconds,
					LoadedIcons = icons.Count(static icon => !icon.IsEmpty), IconResults = entries.Select((entry, index) => new { entry.Id, entry.Title, Bytes = icons[index].Length }).ToArray(),
					Entries = session.Entries.Select(Describe).ToArray(),
				}));
			}
		}
	}

	private static object Describe(WindowsShellContextMenuEntry entry)
	{
		return new { entry.Id, entry.Title, entry.IsEnabled, entry.IsChecked, entry.IsSeparator, entry.HasIcon, Children = entry.Children.Select(Describe).ToArray() };
	}

	private static IEnumerable<WindowsShellContextMenuEntry> Flatten(IEnumerable<WindowsShellContextMenuEntry> entries)
	{
		foreach (var entry in entries)
		{
			yield return entry;
			foreach (var child in Flatten(entry.Children))
			{
				yield return child;
			}
		}
	}
}

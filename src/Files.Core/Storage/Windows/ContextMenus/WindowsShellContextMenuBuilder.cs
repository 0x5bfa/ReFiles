// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Storage.Windows;

/// <summary>Constructs normalized menu hierarchies from application and Windows command contributions.</summary>
public static class WindowsShellContextMenuBuilder
{
	/// <summary>Constructs a menu when no Windows selection session is available.</summary>
	/// <param name="applicationEntries">The application-owned menu entries.</param>
	/// <param name="expandedMenuEntry">The optional entry for the expanded native menu.</param>
	/// <returns>The completed application menu with normalized separators.</returns>
	public static IReadOnlyList<WindowsShellContextMenuEntry> BuildApplicationMenu(IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries, WindowsShellContextMenuEntry? expandedMenuEntry = null)
	{
		ArgumentNullException.ThrowIfNull(applicationEntries);

		return Build(applicationEntries, [], [], expandedMenuEntry);
	}

	internal static IReadOnlyList<WindowsShellContextMenuEntry> Build(IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries,
		IReadOnlyList<WindowsShellContextMenuEntry> cloudVerbs, IReadOnlyList<WindowsShellContextMenuGroup> groups, WindowsShellContextMenuEntry? expandedMenuEntry)
	{
		var entries = new List<WindowsShellContextMenuEntry>();
		Append(entries, applicationEntries);
		var cloudGroups = groups.Where(static group => group.IsCloudProvider).OrderBy(static group => group.Title, StringComparer.OrdinalIgnoreCase).ToArray();
		var otherGroups = groups.Where(static group => !group.IsCloudProvider).OrderBy(static group => group.Title, StringComparer.OrdinalIgnoreCase).ToArray();
		AppendSection(entries, cloudVerbs.Concat(cloudGroups.SelectMany(CreateGroupEntries)));
		AppendSection(entries, otherGroups.SelectMany(CreateGroupEntries));
		if (expandedMenuEntry is not null)
		{
			AppendSection(entries, [expandedMenuEntry]);
		}

		RemoveTrailingSeparator(entries);

		return Array.AsReadOnly(entries.ToArray());
	}

	private static IEnumerable<WindowsShellContextMenuEntry> CreateGroupEntries(WindowsShellContextMenuGroup group)
	{
		var children = new List<WindowsShellContextMenuEntry>();
		Append(children, group.Entries);
		RemoveTrailingSeparator(children);
		if (children.Count is 0)
		{
			return [];
		}

		if (!group.IsCloudProvider && children.Count(static child => !child.IsSeparator) is 1)
		{
			return children;
		}

		if (string.IsNullOrWhiteSpace(group.Title))
		{
			return children;
		}

		return [new(group.Key, group.Title, children.Any(static child => !child.IsSeparator && child.IsEnabled), children: children, icon: group.Icon)];
	}

	private static void AppendSection(List<WindowsShellContextMenuEntry> destination, IEnumerable<WindowsShellContextMenuEntry> source)
	{
		var entries = source.ToArray();
		if (!entries.Any(static entry => !entry.IsSeparator))
		{
			return;
		}

		if (destination.Count is not 0)
		{
			Append(destination, [WindowsShellContextMenuEntry.CreateSeparator()]);
		}

		Append(destination, entries);
	}

	private static void Append(List<WindowsShellContextMenuEntry> destination, IEnumerable<WindowsShellContextMenuEntry> entries)
	{
		foreach (var entry in entries)
		{
			if (!entry.IsSeparator || destination.Count is not 0 && !destination[^1].IsSeparator)
			{
				destination.Add(entry);
			}
		}
	}

	private static void RemoveTrailingSeparator(List<WindowsShellContextMenuEntry> entries)
	{
		if (entries.Count is not 0 && entries[^1].IsSeparator)
		{
			entries.RemoveAt(entries.Count - 1);
		}
	}
}

internal sealed record WindowsShellContextMenuGroup(string Key, string Title, bool IsCloudProvider, IReadOnlyList<WindowsShellContextMenuEntry> Entries, WindowsShellContextMenuIcon? Icon = null);

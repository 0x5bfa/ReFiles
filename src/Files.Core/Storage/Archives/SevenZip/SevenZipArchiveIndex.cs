// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using global::SevenZip;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveIndex
{
	private const string NamelessEntryPlaceholder = "[no name]";
	private readonly IReadOnlyDictionary<string, SevenZipArchiveNode> nodes;
	private readonly IReadOnlyDictionary<
		string,
		IReadOnlyList<SevenZipArchiveNode>> children;

	private SevenZipArchiveIndex(
		IReadOnlyDictionary<string, SevenZipArchiveNode> nodes,
		IReadOnlyDictionary<
			string,
			IReadOnlyList<SevenZipArchiveNode>> children)
	{
		this.nodes = nodes;
		this.children = children;
	}

	public static SevenZipArchiveIndex Create(IEnumerable<ArchiveFileInfo> entries, string rootName)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentException.ThrowIfNullOrWhiteSpace(rootName);

		var mutableNodes =
			new Dictionary<string, SevenZipArchiveNode>(StringComparer.Ordinal)
			{
				[string.Empty] = new(string.Empty, rootName, IsDirectory: true, EntryIndex: null, Size: 0),
			};

		foreach (var entry in entries)
		{
			var rawPath = GetEntryName(entry, rootName);
			if (!ArchiveEntryPath.TryNormalize(rawPath, out var entryPath)
				|| string.IsNullOrEmpty(entryPath))
			{
				continue;
			}

			EnsureParentFolders(mutableNodes, entryPath);

			var isDirectory = entry.IsDirectory
				|| rawPath.EndsWith('/')
				|| rawPath.EndsWith('\\');
			mutableNodes[entryPath] = new SevenZipArchiveNode(
				entryPath,
				ArchiveEntryPath.GetName(entryPath),
				isDirectory,
				isDirectory ? null : entry.Index,
				entry.Size);
		}

		PromoteParentsToFolders(mutableNodes);

		var mutableChildren =
			new Dictionary<string, List<SevenZipArchiveNode>>(StringComparer.Ordinal);
		foreach (var node in mutableNodes.Values)
		{
			if (string.IsNullOrEmpty(node.Path))
			{
				continue;
			}

			var parent = ArchiveEntryPath.GetParent(node.Path);
			if (!mutableChildren.TryGetValue(parent, out var childNodes))
			{
				childNodes = [];
				mutableChildren.Add(parent, childNodes);
			}

			childNodes.Add(node);
		}

		var immutableChildren = mutableChildren.ToDictionary(
			static pair => pair.Key,
			static pair =>
				(IReadOnlyList<SevenZipArchiveNode>)Array.AsReadOnly(
					pair.Value
						.OrderByDescending(static node => node.IsDirectory)
						.ThenBy(static node => node.Name, StringComparer.OrdinalIgnoreCase)
						.ThenBy(static node => node.Name, StringComparer.Ordinal)
						.ToArray()),
			StringComparer.Ordinal);

		return new SevenZipArchiveIndex(new Dictionary<string, SevenZipArchiveNode>(mutableNodes, StringComparer.Ordinal), immutableChildren);
	}

	public SevenZipArchiveNode GetNode(string entryPath)
	{
		var normalizedPath = ArchiveEntryPath.Normalize(entryPath);
		return nodes.TryGetValue(normalizedPath, out var node)
			? node
			: throw new FileNotFoundException($"Archive entry '{normalizedPath}' was not found.", normalizedPath);
	}

	public IReadOnlyList<SevenZipArchiveNode> GetChildren(string entryPath)
	{
		var normalizedPath = ArchiveEntryPath.Normalize(entryPath);
		return children.TryGetValue(normalizedPath, out var childNodes)
			? childNodes
			: [];
	}

	private static void PromoteParentsToFolders(IDictionary<string, SevenZipArchiveNode> nodes)
	{
		var parentPaths = nodes.Keys
			.Select(ArchiveEntryPath.GetParent)
			.Where(static path => !string.IsNullOrEmpty(path))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		foreach (var parentPath in parentPaths)
		{
			nodes[parentPath] = new SevenZipArchiveNode(parentPath, ArchiveEntryPath.GetName(parentPath), IsDirectory: true, EntryIndex: null, Size: 0);
		}
	}

	private static void EnsureParentFolders(IDictionary<string, SevenZipArchiveNode> nodes, string entryPath)
	{
		var parentPath = ArchiveEntryPath.GetParent(entryPath);
		while (!string.IsNullOrEmpty(parentPath))
		{
			nodes[parentPath] = new SevenZipArchiveNode(parentPath, ArchiveEntryPath.GetName(parentPath), IsDirectory: true, EntryIndex: null, Size: 0);
			parentPath = ArchiveEntryPath.GetParent(parentPath);
		}
	}

	private static string GetEntryName(ArchiveFileInfo entry, string rootName)
	{
		if (!string.IsNullOrEmpty(entry.FileName)
			&& !entry.FileName.Equals(NamelessEntryPlaceholder, StringComparison.Ordinal))
		{
			return entry.FileName;
		}

		var fallback = Path.GetFileNameWithoutExtension(rootName);
		return string.IsNullOrWhiteSpace(fallback)
			? NamelessEntryPlaceholder
			: fallback;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Archives;

internal sealed class ArchiveCatalog
{
	private readonly Dictionary<string, ArchiveNode> _nodes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ArchiveNode> _folders = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<ArchiveNode>> _children = new(StringComparer.Ordinal);

	public IReadOnlyList<ArchiveEntryInfo> Entries { get; }

	public ArchiveNode Root => _folders[string.Empty];

	public ArchiveCatalog(IEnumerable<ArchiveEntryInfo> entries, string name)
	{
		Entries = Array.AsReadOnly(entries.ToArray());
		AddFolder(string.Empty, name);
		var recordIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var entry in Entries)
		{
			if (string.IsNullOrEmpty(entry.Id) || !recordIds.Add(entry.Id))
			{
				throw new InvalidDataException("Archive readers must provide unique, nonempty record identifiers.");
			}

			if (!ArchiveEntryPath.TryNormalize(entry.Path, out var path) || string.IsNullOrEmpty(path))
			{
				continue;
			}

			EnsureFolder(ArchiveEntryPath.GetParent(path));
			if (entry.IsDirectory)
			{
				EnsureFolder(path);
			}
			else
			{
				var node = new ArchiveNode($"entry:{entry.Id}", path, ArchiveEntryPath.GetName(path), false, entry.Id);
				_nodes.Add(node.Id, node);
				_children[ArchiveEntryPath.GetParent(path)].Add(node);
			}
		}
	}

	public ArchiveNode GetNode(string id)
	{
		return _nodes.TryGetValue(id, out var node) ? node : throw new FileNotFoundException($"Archive item '{id}' was not found.");
	}

	public ArchiveNode GetFolder(string path)
	{
		var normalized = ArchiveEntryPath.Normalize(path);

		return _folders.TryGetValue(normalized, out var node) ? node : throw new DirectoryNotFoundException($"Archive folder '{normalized}' was not found.");
	}

	public IReadOnlyList<ArchiveNode> GetChildren(string path)
	{
		return _children[GetFolder(path).Path];
	}

	private void EnsureFolder(string path)
	{
		var missing = new Stack<string>();
		while (!_folders.ContainsKey(path))
		{
			missing.Push(path);
			path = ArchiveEntryPath.GetParent(path);
		}

		while (missing.TryPop(out var folder))
		{
			AddFolder(folder, ArchiveEntryPath.GetName(folder));
		}
	}

	private void AddFolder(string path, string name)
	{
		var node = new ArchiveNode($"folder:{path}", path, name, true, null);
		_nodes.Add(node.Id, node);
		_folders.Add(path, node);
		_children.Add(path, []);
		if (path.Length != 0)
		{
			_children[ArchiveEntryPath.GetParent(path)].Add(node);
		}
	}
}

internal sealed record ArchiveNode(string Id, string Path, string Name, bool IsDirectory, string? EntryId);

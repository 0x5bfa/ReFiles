// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives;

internal sealed class WindowsShellArchiveMount : IArchiveMount
{
	public string BackendId =>
		WindowsShellArchiveBackend.DefaultBackendId;

	public StorableReference Archive { get; }

	public IStorageSource ItemSource { get; }

	public IFolder Root { get; }

	public WindowsShellArchiveMount(StorableReference archive, IStorageSource itemSource, IFolder root)
	{
		ArgumentNullException.ThrowIfNull(archive);
		ArgumentNullException.ThrowIfNull(itemSource);
		ArgumentNullException.ThrowIfNull(root);

		Archive = archive;
		ItemSource = itemSource;
		Root = root;
	}

	public async ValueTask<IStorable> ResolveAsync(string entryPath, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var normalizedPath = ArchiveEntryPath.Normalize(entryPath);
		if (string.IsNullOrEmpty(normalizedPath))
		{
			return Root;
		}

		IStorable current = Root;
		foreach (var segment in normalizedPath.Split('/'))
		{
			if (current is not IFolder folder)
			{
				throw new DirectoryNotFoundException($"Archive entry '{normalizedPath}' is not a folder.");
			}

			IStorable? match = null;
			await foreach (var child in folder .GetItemsAsync(StorableType.All, cancellationToken) .ConfigureAwait(false))
			{
				if (child.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
				{
					match = child;
					break;
				}
			}

			current = match
				?? throw new FileNotFoundException($"Archive entry '{normalizedPath}' was not found.", normalizedPath);
		}

		return current;
	}

	public ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);

		return ValueTask.CompletedTask;
	}
}

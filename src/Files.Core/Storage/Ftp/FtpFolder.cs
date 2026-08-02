// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using OwlCore.Storage;

namespace Files.Core.Storage.Ftp;

public sealed class FtpFolder : FtpStorable, IChildFolder
{
	internal FtpFolder(FtpStorageSource source, FtpStorableSnapshot snapshot, FtpStorableFactory factory)
		: base(source, snapshot, factory)
	{
	}

	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (type is StorableType.None)
		{
			yield break;
		}

		var entries = await Factory.GetItemsAsync(Path, cancellationToken).ConfigureAwait(false);
		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var include = entry.Kind is FtpEntryKind.Folder
				? type.HasFlag(StorableType.Folder)
				: type.HasFlag(StorableType.File);
			if (include)
			{
				yield return Factory.Create(entry);
			}
		}
	}
}

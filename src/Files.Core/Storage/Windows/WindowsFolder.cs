// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

public sealed class WindowsFolder : WindowsStorable, IChildFolder
{
	private const int EnumerationBatchSize = 32;

	internal WindowsFolder(
		WindowsStorableDescriptor descriptor,
		WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(
		StorableType type = StorableType.All,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (type is StorableType.None)
		{
			yield break;
		}

		await using var enumerator = await Factory
			.CreateEnumeratorAsync(Descriptor, cancellationToken)
			.ConfigureAwait(false);

		while (true)
		{
			var descriptors = await enumerator
				.ReadNextAsync(EnumerationBatchSize, cancellationToken)
				.ConfigureAwait(false);

			if (descriptors.Count is 0)
			{
				yield break;
			}

			foreach (var descriptor in descriptors)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var include = descriptor.Snapshot.IsFolder
					? type.HasFlag(StorableType.Folder)
					: type.HasFlag(StorableType.File);

				if (include)
				{
					yield return Factory.Create(descriptor);
				}
			}
		}
	}
}

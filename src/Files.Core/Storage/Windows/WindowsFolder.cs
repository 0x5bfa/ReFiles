// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

public sealed class WindowsFolder : WindowsStorable, IChildFolder
{
	internal WindowsFolder(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	/// <summary>
	/// Gets the column metadata exposed by this Shell folder.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The Shell columns and the columns enabled by default.</returns>
	public Task<WindowsShellColumnSet> GetColumnsAsync(CancellationToken cancellationToken = default)
	{
		return Factory.GetColumnsAsync(Descriptor, cancellationToken);
	}

	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (type is StorableType.None)
		{
			yield break;
		}

		await foreach (var descriptor in Factory .EnumerateChildrenAsync(Descriptor, cancellationToken) .ConfigureAwait(false))
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

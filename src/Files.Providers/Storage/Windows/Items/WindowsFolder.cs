// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.CompilerServices;
using Windows.Win32.Foundation;

namespace Files.Core.Storage.Windows;

/// <summary>Represents a folder exposed by the Windows Shell.</summary>
internal sealed class WindowsFolder : WindowsStorable, IFolder, IInteractiveFolder
{
	private readonly WindowsFolderEnumerator _enumerator;

	internal WindowsFolder(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory, WindowsFolderEnumerator enumerator)
		: base(descriptor, factory)
	{
		ArgumentNullException.ThrowIfNull(enumerator);

		_enumerator = enumerator;
	}

	/// <summary>Enumerates the items in the Windows Shell folder.</summary>
	/// <param name="type">The kinds of items to include.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>An asynchronous sequence of child items.</returns>
	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var item in GetItemsAsync(type, 0, cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type, nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (type is StorableType.None)
		{
			yield break;
		}

		await foreach (var descriptor in _enumerator.EnumerateChildrenAsync(Descriptor, new HWND(ownerWindowHandle), cancellationToken).ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var include = descriptor.Metadata.IsFolder
				? type.HasFlag(StorableType.Folder)
				: type.HasFlag(StorableType.File);

			if (include)
			{
				yield return descriptor.Metadata.IsFolder
					? new WindowsFolder(descriptor, Factory, _enumerator)
					: new WindowsFile(descriptor, Factory);
			}
		}
	}
}

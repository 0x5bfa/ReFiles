// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Capabilities;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>Adapts an OwlCore folder to the Files folder model contract.</summary>
public sealed class FolderModel : StorableModel, IFolderModel
{
	private readonly IFolder _folder;

	private readonly IStorageSource _source;

	private readonly IStorableModelFactory _modelFactory;

	/// <summary>
	/// Initializes a Files folder model.
	/// </summary>
	/// <param name="source">The storage source that owns the folder.</param>
	/// <param name="folder">The owned OwlCore folder.</param>
	/// <param name="modelFactory">The factory used to adapt child items.</param>
	/// <param name="reference">The stable Files item reference.</param>
	/// <param name="capabilities">The owned composed item capabilities.</param>
	public FolderModel(IStorageSource source, IFolder folder, IStorableModelFactory modelFactory, StorableReference reference, ICapabilities capabilities)
		: base(folder, reference, capabilities)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(modelFactory);

		_source = source;
		_folder = folder;
		_modelFactory = modelFactory;
	}

	/// <summary>
	/// Enumerates child item models owned by the caller.
	/// </summary>
	/// <param name="type">The item types to include.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>The child item models.</returns>
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var item in GetItemsCoreAsync(type, 0, cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <summary>
	/// Gets the parent folder model when the storage source exposes one.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The parent folder model owned by the caller, or <see langword="null"/> for a root.</returns>
	public async ValueTask<IFolderModel?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		if (_folder is not IStorableChild child)
		{
			return null;
		}

		var parent = await child.GetParentAsync(cancellationToken).ConfigureAwait(false);
		if (parent is null)
		{
			return null;
		}

		var model = _modelFactory.Create(_source, parent);
		if (model is IFolderModel folder)
		{
			return folder;
		}

		await model.DisposeAsync().ConfigureAwait(false);
		throw new InvalidOperationException($"The parent of '{Reference.ItemId}' is not a folder.");
	}

	internal async IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type, nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var item in GetItemsCoreAsync(type, ownerWindowHandle, cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	private async IAsyncEnumerable<IStorableModel> GetItemsCoreAsync(StorableType type, nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		var items = ownerWindowHandle is not 0 && _folder is WindowsFolder windowsFolder
			? windowsFolder.GetItemsAsync(type, ownerWindowHandle, cancellationToken)
			: _folder.GetItemsAsync(type, cancellationToken);
		await foreach (var item in items.ConfigureAwait(false))
		{
			yield return _modelFactory.Create(_source, item);
		}
	}
}

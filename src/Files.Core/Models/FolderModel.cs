// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public sealed class FolderModel : StorableModel, IFolderModel
{
	private readonly IStorageSource _source;

	private readonly IStorableModelFactory _modelFactory;

	public IFolder Folder { get; }

	public FolderModel(IStorageSource source, IFolder folder, IStorableModelFactory modelFactory, StorableReference reference, IItemFeatures features)
		: base(folder, reference, features)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(modelFactory);

		_source = source;
		_modelFactory = modelFactory;
		Folder = folder;
	}

	public async IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		await foreach (var item in Folder.GetItemsAsync(type, cancellationToken).ConfigureAwait(false))
		{
			yield return _modelFactory.Create(_source, item);
		}
	}

	public async ValueTask<IFolderModel?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		if (Folder is not IStorableChild child)
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
}

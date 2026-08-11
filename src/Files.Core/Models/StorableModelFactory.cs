// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>Creates storage presentation models and composes their item features.</summary>
public sealed class StorableModelFactory : IStorableModelFactory
{
	private readonly ItemFeatureRegistry _itemFeatureRegistry;

	/// <summary>Initializes a model factory.</summary>
	/// <param name="itemFeatureRegistry">The optional item feature registry.</param>
	public StorableModelFactory(ItemFeatureRegistry? itemFeatureRegistry = null)
	{
		_itemFeatureRegistry = itemFeatureRegistry ?? ItemFeatureRegistry.Empty;
	}

	/// <summary>Creates a presentation model for a storage-layer item.</summary>
	/// <param name="source">The storage source that owns the item.</param>
	/// <param name="coreModel">The storage-layer item.</param>
	/// <returns>The corresponding presentation model.</returns>
	public IStorableModel Create(IStorageSource source, IStorable coreModel)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(coreModel);

		IItemFeatures? features = null;

		try
		{
			var reference = new StorableReference(source.SourceId, coreModel.Id, (coreModel as IStorageAddressSource)?.Address);
			var context = new ItemContext(source, coreModel, reference);
			features = _itemFeatureRegistry.CreateFeatures(context);

			return coreModel switch
			{
				IFile file => new FileModel(file, reference, features),
				IFolder folder => new FolderModel(source, folder, this, reference, features),
				_ => new StorableModel(coreModel, reference, features),
			};
		}
		catch (Exception creationError)
		{
			var cleanupErrors = new List<Exception>();
			if (features is not null)
			{
				TryDisposeSynchronously(features, cleanupErrors);
			}

			TryDisposeSynchronously(coreModel, cleanupErrors);
			if (cleanupErrors.Count is 0)
			{
				throw;
			}

			cleanupErrors.Insert(0, creationError);
			throw new AggregateException("Storable model construction and cleanup failed.", cleanupErrors);
		}
	}

	private static void TryDisposeSynchronously(object instance, ICollection<Exception> errors)
	{
		try
		{
			if (instance is IAsyncDisposable asyncDisposable)
			{
				asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
			}
			else
			{
				(instance as IDisposable)?.Dispose();
			}
		}
		catch (Exception error)
		{
			errors.Add(error);
		}
	}
}

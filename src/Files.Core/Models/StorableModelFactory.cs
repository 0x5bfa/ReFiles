// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public sealed class StorableModelFactory : IStorableModelFactory
{
	private readonly ItemFeatureRegistry itemFeatureRegistry;

	public StorableModelFactory(ItemFeatureRegistry? itemFeatureRegistry = null)
	{
		this.itemFeatureRegistry = itemFeatureRegistry ?? ItemFeatureRegistry.Empty;
	}

	public IStorableModel Create(IStorageSource source, IStorable coreModel)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(coreModel);

		IItemFeatures? features = null;

		try
		{
			var reference = new StorableReference(
				source.SourceId,
				coreModel.Id,
				(coreModel as IStorageAddressSource)?.Address);
			var context = new ItemContext(source, coreModel, reference);
			features = itemFeatureRegistry.CreateFeatures(context);

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
			throw new AggregateException(
				"Storable model construction and cleanup failed.",
				cleanupErrors);
		}
	}

	private static void TryDisposeSynchronously(
		object instance,
		ICollection<Exception> errors)
	{
		try
		{
			if (instance is IAsyncDisposable asyncDisposable)
			{
				asyncDisposable
					.DisposeAsync()
					.AsTask()
					.GetAwaiter()
					.GetResult();
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

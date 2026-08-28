// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>Creates storage presentation models and composes their item capabilities.</summary>
public sealed class StorableModelFactory : IStorableModelFactory
{
	private readonly CapabilityRegistry _capabilityRegistry;

	/// <summary>Initializes a model factory.</summary>
	/// <param name="capabilityRegistry">The optional item capability registry.</param>
	public StorableModelFactory(CapabilityRegistry? capabilityRegistry = null)
	{
		_capabilityRegistry = capabilityRegistry ?? CapabilityRegistry.Empty;
	}

	/// <summary>Creates a presentation model for a storage-layer item.</summary>
	/// <param name="source">The storage source that owns the item.</param>
	/// <param name="coreModel">The storage-layer item.</param>
	/// <returns>The corresponding presentation model.</returns>
	public IStorableModel Create(IStorageSource source, IStorable coreModel)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(coreModel);

		ICapabilities? capabilities = null;

		try
		{
			var reference = new StorableReference(source.SourceId, coreModel.Id, (coreModel as IStorageAddressSource)?.Address);
			var context = new ItemContext(source, coreModel, reference);
			capabilities = _capabilityRegistry.CreateCapabilities(context);

			return coreModel switch
			{
				IFile file => new FileModel(file, reference, capabilities),
				IFolder folder => new FolderModel(source, folder, this, reference, capabilities),
				_ => new StorableModel(coreModel, reference, capabilities),
			};
		}
		catch (Exception creationError)
		{
			var cleanupErrors = new List<Exception>();
			if (capabilities is not null)
			{
				TryDisposeSynchronously(capabilities, cleanupErrors);
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

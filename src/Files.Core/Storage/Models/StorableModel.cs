// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Storage;

namespace Files.Core.Models;

/// <summary>
/// Adapts a Files storage item to the Files item AppModel contract.
/// </summary>
public class StorableModel : IStorableModel, IStorableModelInternal
{
	private readonly IStorable _coreModel;
	private readonly IOwnedCapabilities _ownedCapabilities;

	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	/// <summary>
	/// Gets the stable Files reference for the item.
	/// </summary>
	public StorableReference Reference { get; }

	/// <summary>
	/// Gets the item name captured when the model was created.
	/// </summary>
	public string Name { get; }

	/// <summary>Gets a reopenable locator for this item, including archive record provenance.</summary>
	public StorageItemLocator Locator => _coreModel is Files.Core.Storage.Archives.IArchiveEntry entry
		? this is IFolderModel ? new ArchiveFolderLocator(entry.Locator, entry.EntryPath) : new ArchiveRecordLocator(entry.Locator, entry.EntryId, entry.EntryPath)
		: new ConfiguredItemLocator(Reference);

	/// <summary>
	/// Gets a value indicating whether the storage item is marked as hidden.
	/// </summary>
	public bool IsHidden => _coreModel is IStorageItemAttributes { IsHidden: true };

	/// <summary>
	/// Gets the composed optional item capabilities.
	/// </summary>
	public ICapabilities Capabilities { get; }

	/// <summary>
	/// Initializes a Files item model.
	/// </summary>
	/// <param name="coreModel">The owned Files storage item.</param>
	/// <param name="reference">The stable Files item reference.</param>
	/// <param name="capabilities">The owned composed item capabilities.</param>
	public StorableModel(IStorable coreModel, StorableReference reference, IOwnedCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(coreModel);

		ArgumentNullException.ThrowIfNull(reference);

		ArgumentNullException.ThrowIfNull(capabilities);

		_coreModel = coreModel;
		Reference = reference;
		Name = coreModel.Name;
		_ownedCapabilities = capabilities;
		Capabilities = new CapabilityReader(capabilities);
	}

	/// <summary>
	/// Synchronously disposes the item model.
	/// </summary>
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>
	/// Asynchronously disposes the item capabilities and owned storage item.
	/// </summary>
	/// <returns>A task that represents the asynchronous disposal operation.</returns>
	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			if (_disposeTask is null)
			{
				_isDisposed = true;
				_disposeTask = DisposeAsyncCore().AsTask();
			}

			return new ValueTask(_disposeTask);
		}
	}

	/// <summary>
	/// Throws when the model has begun disposal.
	/// </summary>
	protected void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

	/// <summary>
	/// Disposes resources owned by this model.
	/// </summary>
	/// <returns>A task that represents the asynchronous disposal operation.</returns>
	protected virtual async ValueTask DisposeAsyncCore()
	{
		List<Exception>? errors = null;

		try
		{
			await _ownedCapabilities.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			(errors ??= []).Add(error);
		}

		try
		{
			if (_coreModel is IAsyncDisposable asyncDisposableCoreModel)
			{
				await asyncDisposableCoreModel.DisposeAsync().ConfigureAwait(false);
			}
			else if (_coreModel is IDisposable disposableCoreModel)
			{
				disposableCoreModel.Dispose();
			}
		}
		catch (Exception error)
		{
			(errors ??= []).Add(error);
		}

		GC.SuppressFinalize(this);

		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("One or more storable model resources could not be disposed.", errors);
		}
	}

	IStorable IStorableModelInternal.GetCoreModel()
	{
		ThrowIfDisposed();

		return _coreModel;
	}
}

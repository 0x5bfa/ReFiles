// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>
/// Adapts an OwlCore storage item to the Files item AppModel contract.
/// </summary>
public class StorableModel : IStorableModel, IStorableModelInternal
{
	private readonly IStorable _coreModel;

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

	/// <summary>
	/// Gets the composed optional item features.
	/// </summary>
	public IItemFeatures Features { get; }

	/// <summary>
	/// Initializes a Files item model.
	/// </summary>
	/// <param name="coreModel">The owned OwlCore storage item.</param>
	/// <param name="reference">The stable Files item reference.</param>
	/// <param name="features">The owned composed item features.</param>
	public StorableModel(IStorable coreModel, StorableReference reference, IItemFeatures features)
	{
		ArgumentNullException.ThrowIfNull(coreModel);
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(features);

		_coreModel = coreModel;
		Reference = reference;
		Name = coreModel.Name;
		Features = features;
	}

	/// <summary>
	/// Synchronously disposes the item model.
	/// </summary>
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>
	/// Asynchronously disposes the item features and owned storage item.
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
			await Features.DisposeAsync().ConfigureAwait(false);
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

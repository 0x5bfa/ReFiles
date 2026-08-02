// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public class StorableModel : IStorableModel
{
	private readonly Lock _disposalLock = new();

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	public IStorable CoreModel { get; }

	public StorableReference Reference { get; }

	public string Name { get; }

	public IItemFeatures Features { get; }

	public StorableModel(IStorable coreModel, StorableReference reference, IItemFeatures features)
	{
		ArgumentNullException.ThrowIfNull(coreModel);
		ArgumentNullException.ThrowIfNull(reference);
		ArgumentNullException.ThrowIfNull(features);

		CoreModel = coreModel;
		Reference = reference;
		Name = coreModel.Name;
		Features = features;
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

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

	protected void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

	}

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
			if (CoreModel is IAsyncDisposable asyncDisposableCoreModel)
			{
				await asyncDisposableCoreModel.DisposeAsync().ConfigureAwait(false);
			}
			else if (CoreModel is IDisposable disposableCoreModel)
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
}

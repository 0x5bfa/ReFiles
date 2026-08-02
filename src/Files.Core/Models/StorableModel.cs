// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public class StorableModel : IStorableModel
{
	private readonly object disposalLock = new();
	private Task? disposeTask;
	private volatile bool isDisposed;

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

	public IStorable CoreModel { get; }

	public StorableReference Reference { get; }

	public string Name { get; }

	public IItemFeatures Features { get; }

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			if (disposeTask is null)
			{
				isDisposed = true;
				disposeTask = DisposeAsyncCore().AsTask();
			}

			return new ValueTask(disposeTask);
		}
	}

	protected void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
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

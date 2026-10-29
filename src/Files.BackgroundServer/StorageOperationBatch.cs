// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.BackgroundServer;

internal sealed class StorageOperationBatch : IServerOperation
{
	private readonly StorageOperationExecutor _executor;
	private readonly StorageOperationData[] _items;

	public Guid Id { get; }
	public string Kind => _items[0].Kind.ToString();
	public int ItemCount => _items.Length;
	public string? DestinationPath => _items[0].Destination.AddressValue;
	public bool CanPause => _items[0].Kind is StorageOperationKind.Copy or StorageOperationKind.Move;

	internal StorageOperationBatch(Guid id, StorageOperationData[] items, StorageOperationExecutor executor)
	{
		if (items.Length is 0 or > 10000)
		{
			throw new ArgumentException("A storage batch must contain between 1 and 10000 items.", nameof(items));
		}

		_executor = executor;
		_items = (StorageOperationData[])items.Clone();
		foreach (var item in _items)
		{
			StorageOperationExecutor.Validate(item);
			if (item.Kind != _items[0].Kind)
			{
				throw new ArgumentException("All items in a storage batch must have the same operation kind.", nameof(items));
			}
		}

		Id = id;
	}

	public async Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken)
	{
		for (var index = 0; index < _items.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var item = _items[index];
			context.Report(new OperationProgress(index, _items.Length, CurrentItemName: item.DisplayName));
			var request = await _executor.CreateRequestAsync(item, cancellationToken).ConfigureAwait(false);
			var progress = new StorageOperationProgressReporter(context, index, _items.Length, item.DisplayName);
			var result = await _executor.ExecuteAsync(request, progress, context, cancellationToken).ConfigureAwait(false);
			context.SetResult(result);
			if (!result.Succeeded)
			{
				throw result.Error ?? new IOException("The storage operation failed.");
			}

			context.Report(new OperationProgress(index + 1, _items.Length, CurrentItemName: item.DisplayName));
		}
	}
}

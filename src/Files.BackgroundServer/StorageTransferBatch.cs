// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.BackgroundServer;

internal sealed class StorageTransferBatch : IServerOperation
{
	private readonly StorageOperationExecutor _executor;
	private readonly StorageOperationRequest[] _requests;
	private readonly string[] _names;
	public Guid Id { get; }
	public string Kind { get; }
	public int ItemCount => _requests.Length;
	public string? DestinationPath { get; }
	public bool CanPause => true;

	internal StorageTransferBatch(Guid id, StorageTransferData[] items, ArchiveLocatorStepData[] steps, StorageOperationExecutor executor)
	{
		ArgumentNullException.ThrowIfNull(items);

		ArgumentNullException.ThrowIfNull(steps);

		if (items.Length is 0 or > 10000 || steps.Length > 310000 || items.Any(item => item.Kind != items[0].Kind))
		{
			throw new ArgumentException("A transfer batch requires bounded, homogeneous item values.", nameof(items));
		}

		_executor = executor;
		_requests = items.Select(item => CreateRequest(item, steps)).ToArray();
		if (_requests.Any(request => !_executor.CanHandle(request)))
		{
			throw new NotSupportedException("The server has no configured route for this transfer.");
		}

		_names = items.Select(item => item.DisplayName ?? string.Empty).ToArray();
		Id = id;
		Kind = items[0].Kind.ToString();
		DestinationPath = items[0].Destination.AddressValue;
	}

	public async Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken)
	{
		for (var index = 0; index < _requests.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			context.Report(new OperationProgress(index, ItemCount, CurrentItemName: _names[index]));
			var progress = new StorageOperationProgressReporter(context, index, ItemCount, _names[index]);
			var result = await _executor.ExecuteAsync(_requests[index], progress, context, cancellationToken).ConfigureAwait(false);
			context.SetResult(result);
			if (!result.Succeeded)
			{
				throw result.Error ?? new IOException("The transfer failed.");
			}

			context.Report(new OperationProgress(index + 1, ItemCount, CurrentItemName: _names[index]));
		}
	}

	internal static StorageOperationRequest CreateRequest(StorageTransferData data, ArchiveLocatorStepData[] steps)
	{
		if (data.Kind is not StorageOperationKind.Copy and not StorageOperationKind.Move || !Enum.IsDefined((StorageConflictBehavior)data.ConflictBehavior)
			|| data.ArchiveStepCount is < 0 or > 31 || data.ArchiveStepOffset < 0 || data.ArchiveStepOffset > steps.Length - data.ArchiveStepCount)
		{
			throw new ArgumentException("The transfer kind, conflict policy, or container chain is invalid.", nameof(data));
		}

		var root = GetReference(data.Root);
		var destination = GetReference(data.Destination);
		StorageItemLocator source;
		if (data.SourceKind == StorageTransferSourceKind.ConfiguredItem)
		{
			if (data.ArchiveStepCount != 0 || !string.IsNullOrEmpty(data.RecordId) || !string.IsNullOrEmpty(data.EntryPath))
			{
				throw new ArgumentException("An ordinary item cannot carry archive selectors.", nameof(data));
			}

			source = new ConfiguredItemLocator(root);
		}
		else if (data.SourceKind is StorageTransferSourceKind.ArchiveRecord or StorageTransferSourceKind.ArchiveFolder)
		{
			var archive = new ArchiveLocator(root);
			for (var index = data.ArchiveStepOffset; index < data.ArchiveStepOffset + data.ArchiveStepCount; index++)
			{
				archive = archive.Append(steps[index].RecordId, steps[index].EntryPath);
			}

			source = data.SourceKind == StorageTransferSourceKind.ArchiveRecord ? new ArchiveRecordLocator(archive, data.RecordId, data.EntryPath) : new ArchiveFolderLocator(archive, data.EntryPath);
		}
		else
		{
			throw new ArgumentException("The transfer source kind is invalid.", nameof(data));
		}

		if (data.Kind == StorageOperationKind.Move && source is not ConfiguredItemLocator)
		{
			throw new NotSupportedException("Move from read-only archives is unsupported.");
		}

		var name = string.IsNullOrEmpty(data.Name) ? null : data.Name;

		return data.Kind == StorageOperationKind.Copy ? new CopyOperationRequest(source, destination, name, (StorageConflictBehavior)data.ConflictBehavior)
			: new MoveOperationRequest(source, destination, name, (StorageConflictBehavior)data.ConflictBehavior);
	}

	private static StorableReference GetReference(StorageReferenceData data)
	{
		StorageOperationExecutor.ValidateReference(data);
		if (string.IsNullOrWhiteSpace(data.ItemId))
		{
			throw new ArgumentException("Transfers require a captured item identity.", nameof(data));
		}

		return new StorableReference(new StorageSourceId(data.SourceId), data.ItemId, data.HasAddress ? new StorageAddress(data.AddressScheme, data.AddressValue) : null);
	}
}

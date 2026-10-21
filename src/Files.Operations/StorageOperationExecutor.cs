// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage;
using Files.Core.Windows;

namespace Files.Operations;

internal sealed class StorageOperationExecutor : IAsyncDisposable
{
	private readonly WindowsStorageSource _source = new();
	private readonly WindowsStorageOperationHandler _handler;

	internal WindowsStorageSource Source => _source;

	internal StorageOperationExecutor()
	{
		_handler = new WindowsStorageOperationHandler(_source);
	}

	public ValueTask DisposeAsync()
	{
		return _source.DisposeAsync();
	}

	internal async Task<StorageOperationRequest> CreateRequestAsync(StorageOperationData data, CancellationToken cancellationToken)
	{
		var item = await ResolveReferenceAsync(data.Item, cancellationToken).ConfigureAwait(false);
		var conflict = (StorageConflictBehavior)data.ConflictBehavior;

		return data.Kind switch
		{
			StorageOperationKind.Copy => new CopyOperationRequest(item, await ResolveReferenceAsync(data.Destination, cancellationToken).ConfigureAwait(false), EmptyToNull(data.Name), conflict),
			StorageOperationKind.Move => new MoveOperationRequest(item, await ResolveReferenceAsync(data.Destination, cancellationToken).ConfigureAwait(false), EmptyToNull(data.Name), conflict),
			StorageOperationKind.Delete => new DeleteOperationRequest(item, data.Permanently),
			StorageOperationKind.Rename => new RenameOperationRequest(item, data.Name),
			StorageOperationKind.CreateFile => new CreateItemOperationRequest(item, data.Name, StorageItemKind.File, conflict),
			StorageOperationKind.CreateFolder => new CreateItemOperationRequest(item, data.Name, StorageItemKind.Folder, conflict),
			_ => throw new ArgumentOutOfRangeException(nameof(data)),
		};
	}

	internal ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, StorageOperationProgressReporter progress, OperationExecutionContext control,
		CancellationToken cancellationToken)
	{
		return _handler.ExecuteAsync(request, progress, cancellationToken, control);
	}

	internal static void Validate(StorageOperationData data)
	{
		if (!Enum.IsDefined(data.Kind) || !Enum.IsDefined((StorageConflictBehavior)data.ConflictBehavior))
		{
			throw new ArgumentException("The storage operation kind or conflict behavior is invalid.");
		}

		ValidateReference(data.Item, data.Kind is not StorageOperationKind.Delete);
		if (data.Kind is StorageOperationKind.Copy or StorageOperationKind.Move)
		{
			ValidateReference(data.Destination, true);
		}

		if (data.Kind is StorageOperationKind.Rename or StorageOperationKind.CreateFile or StorageOperationKind.CreateFolder)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(data.Name);
		}
	}

	internal static StorageReferenceData ToData(StorableReference reference)
	{
		return new StorageReferenceData { SourceId = reference.SourceId.Value, ItemId = reference.ItemId, HasAddress = reference.LastKnownAddress is not null,
			AddressScheme = reference.LastKnownAddress?.Scheme ?? string.Empty, AddressValue = reference.LastKnownAddress?.Value ?? string.Empty };
	}

	private async Task<StorableReference> ResolveReferenceAsync(StorageReferenceData data, CancellationToken cancellationToken)
	{
		var address = data.HasAddress ? new StorageAddress(data.AddressScheme, data.AddressValue) : null;
		if (!string.IsNullOrEmpty(data.ItemId))
		{
			return new StorableReference(new StorageSourceId(data.SourceId), data.ItemId, address);
		}

		var item = await _source.ResolveAsync(address!, cancellationToken).ConfigureAwait(false);

		return new StorableReference(_source.SourceId, item.Id, address);
	}

	private static void ValidateReference(StorageReferenceData data, bool requireFileSystem)
	{
		if (data.SourceId != WindowsStorageSource.DefaultSourceType || (!data.HasAddress && string.IsNullOrWhiteSpace(data.ItemId)))
		{
			throw new NotSupportedException("The operation requires a configured Windows storage reference.");
		}

		if (data.HasAddress && (string.IsNullOrWhiteSpace(data.AddressScheme) || string.IsNullOrWhiteSpace(data.AddressValue)))
		{
			throw new ArgumentException("The storage address is incomplete.");
		}

		if (requireFileSystem && (!data.HasAddress || !data.AddressScheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)))
		{
			throw new NotSupportedException("The operation requires a file-system reference.");
		}
	}

	private static string? EmptyToNull(string? value)
	{
		return string.IsNullOrEmpty(value) ? null : value;
	}
}

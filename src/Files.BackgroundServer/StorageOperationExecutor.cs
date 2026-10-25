// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Files.Core.Storage;
using Files.Core.Composition;
using Files.Core.Windows;

namespace Files.BackgroundServer;

internal sealed class StorageOperationExecutor : IAsyncDisposable
{
	private readonly FilesCoreRuntime _runtime;

	internal WindowsStorageSource Source => _runtime.Workspace.Sources.OfType<WindowsStorageSource>().First();

	internal StorageOperationExecutor(FilesCoreRuntime? runtime = null)
	{
		_runtime = runtime ?? new FilesCoreBuilder().AddWindowsStorage(enablePreviews: false, enableArchives: false).Build();
	}

	public ValueTask DisposeAsync()
	{
		return _runtime.DisposeAsync();
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
		return _runtime.StorageOperations.ExecuteAsync(request, progress, cancellationToken, control);
	}

	internal static void Validate(StorageOperationData data)
	{
		if (!Enum.IsDefined(data.Kind) || !Enum.IsDefined((StorageConflictBehavior)data.ConflictBehavior))
		{
			throw new ArgumentException("The storage operation kind or conflict behavior is invalid.");
		}

		ValidateReference(data.Item);
		if (data.Kind is StorageOperationKind.Copy or StorageOperationKind.Move)
		{
			ValidateReference(data.Destination);
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

		await using var item = await _runtime.Workspace.ResolveAsync(new StorageSourceId(data.SourceId), address!, cancellationToken).ConfigureAwait(false);

		return item.Reference;
	}

	private static void ValidateReference(StorageReferenceData data)
	{
		if (string.IsNullOrWhiteSpace(data.SourceId) || (!data.HasAddress && string.IsNullOrWhiteSpace(data.ItemId)))
		{
			throw new NotSupportedException("The operation requires a configured storage reference.");
		}

		if (data.HasAddress && (string.IsNullOrWhiteSpace(data.AddressScheme) || string.IsNullOrWhiteSpace(data.AddressValue)))
		{
			throw new ArgumentException("The storage address is incomplete.");
		}

	}

	private static string? EmptyToNull(string? value)
	{
		return string.IsNullOrEmpty(value) ? null : value;
	}
}

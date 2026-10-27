// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.StorageOperations;

internal static class StorageTransferDataMapper
{
	internal static (BackgroundServer.StorageTransferData[] Items, BackgroundServer.ArchiveLocatorStepData[] Steps) ToData(IReadOnlyList<StorageOperationRequest> requests)
	{
		var steps = new List<BackgroundServer.ArchiveLocatorStepData>();
		var items = new List<BackgroundServer.StorageTransferData>();
		foreach (var request in requests)
		{
			var (source, destination, name, conflict, kind) = request switch
			{
				CopyOperationRequest copy => (copy.Source, copy.DestinationFolder, copy.NewName, copy.ConflictBehavior, BackgroundServer.StorageOperationKind.Copy),
				MoveOperationRequest move => (move.Source, move.DestinationFolder, move.NewName, move.ConflictBehavior, BackgroundServer.StorageOperationKind.Move),
				_ => throw new NotSupportedException("Only Copy and Move have a transfer contract."),
			};
			var container = source switch { ArchiveRecordLocator record => record.Container, ArchiveFolderLocator folder => folder.Container, _ => null };
			var sourceKind = source switch { ConfiguredItemLocator => BackgroundServer.StorageTransferSourceKind.ConfiguredItem,
				ArchiveRecordLocator => BackgroundServer.StorageTransferSourceKind.ArchiveRecord,
				ArchiveFolderLocator => BackgroundServer.StorageTransferSourceKind.ArchiveFolder, _ => throw new NotSupportedException("Unknown transfer source.") };
			var chain = new Stack<ArchiveLocator>();
			for (var current = container; current?.Parent is not null; current = current.Parent)
			{
				chain.Push(current);
			}

			var offset = steps.Count;
			while (chain.TryPop(out var nested))
			{
				steps.Add(new BackgroundServer.ArchiveLocatorStepData { RecordId = nested.EntryId!, EntryPath = nested.EntryPath! });
			}

			var path = source switch { ArchiveRecordLocator record => record.EntryPath, ArchiveFolderLocator folder => folder.EntryPath, _ => string.Empty };
			items.Add(new BackgroundServer.StorageTransferData
			{
				Kind = kind, SourceKind = sourceKind, Root = ToData(source.Root), EntryPath = path,
				RecordId = (source as ArchiveRecordLocator)?.RecordId ?? string.Empty, ArchiveStepOffset = offset, ArchiveStepCount = steps.Count - offset,
				Destination = ToData(destination), Name = name ?? string.Empty, ConflictBehavior = (int)conflict,
				DisplayName = Path.GetFileName((path.Length == 0 ? source.Root.LastKnownAddress?.Value ?? source.Root.ItemId : path).TrimEnd('/', '\\')),
			});
		}

		return (items.ToArray(), steps.ToArray());
	}

	private static BackgroundServer.StorageReferenceData ToData(StorableReference reference)
	{
		return new BackgroundServer.StorageReferenceData { SourceId = reference.SourceId.Value, ItemId = reference.ItemId, HasAddress = reference.LastKnownAddress is not null,
			AddressScheme = reference.LastKnownAddress?.Scheme ?? string.Empty, AddressValue = reference.LastKnownAddress?.Value ?? string.Empty };
	}
}

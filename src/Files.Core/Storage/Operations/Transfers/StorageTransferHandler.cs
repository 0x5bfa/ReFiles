// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage.Archives;

namespace Files.Core.Storage.Transfers;

internal sealed class StorageTransferHandler(StorageWorkspace workspace, ArchiveBackendSelector readers, IArchiveCredentialResolver? credentials, IReadOnlyList<IStorageImportProvider> imports)
	: IStorageOperationHandler
{
	public bool CanHandle(StorageOperationRequest request)
	{
		return request is CopyOperationRequest { Source: ConfiguredItemLocator or ArchiveRecordLocator } copy
			&& workspace.Sources.Any(source => source.SourceId == copy.Source.Root.SourceId)
			&& imports.Any(provider => provider.CanImport(copy.DestinationFolder));
	}

	public async ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default,
		IStorageOperationControl? operationControl = null)
	{
		if (request is not CopyOperationRequest copy || !CanHandle(request))
		{
			return new StorageOperationResult(false, null, new NotSupportedException("The transfer endpoints or operation are unsupported."));
		}

		IStorageImportFile? staged = null;
		long completedBytes = 0;
		var phase = StorageTransferPhase.Reading;
		try
		{
			await using var prepared = await PrepareAsync(copy.Source, cancellationToken).ConfigureAwait(false);
			var item = prepared.Item;
			var content = item.Get<IReadableContent>() ?? throw new NotSupportedException("The selected item has no readable content.");
			if (item is IFolderModel && item.Get<IArchiveSource>() is null)
			{
				throw new NotSupportedException("Folder tree transfers require an explicit traversal implementation.");
			}

			var importer = imports.First(provider => provider.CanImport(copy.DestinationFolder));
			await using (staged = await importer.StageAsync(copy.DestinationFolder, copy.NewName ?? item.Name, copy.ConflictBehavior, cancellationToken)
				.ConfigureAwait(false))
			{
				await using (var input = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false))
				{
					var buffer = new byte[81920];
					var total = input.CanSeek ? input.Length : (long?)null;
					while (true)
					{
						await WaitWhilePausedAsync(operationControl, cancellationToken).ConfigureAwait(false);
						var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
						if (count == 0)
						{
							break;
						}

						await staged.Output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
						completedBytes += count;
						if (total.HasValue)
						{
							total = Math.Max(total.Value, completedBytes);
						}

						progress?.Report(new StorageOperationProgress(0, 1, copy.Source.Root, total.HasValue ? completedBytes : null, total));
					}
				}

				phase = StorageTransferPhase.Finalizing;
				await staged.Output.FlushAsync(cancellationToken).ConfigureAwait(false);
				await WaitWhilePausedAsync(operationControl, cancellationToken).ConfigureAwait(false);
				phase = StorageTransferPhase.Publishing;
				var result = await staged.CommitAsync(operationControl, cancellationToken).ConfigureAwait(false);
				if (result.Succeeded && !result.Skipped)
				{
					phase = StorageTransferPhase.Completed;
					progress?.Report(new StorageOperationProgress(1, 1, result.ResultItem, completedBytes, completedBytes));
				}

				return new StorageOperationResult(result.Succeeded, result.ResultItem, result.Error, result.Skipped, GetOutcome(staged, phase, completedBytes, result.Skipped));
			}
		}
		catch (Exception error)
		{
			if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
			{
				operationControl?.AcknowledgeCancellationRequest();
			}

			return new StorageOperationResult(false, null, error, transfer: GetOutcome(staged, phase, completedBytes));
		}
	}

	private async ValueTask<PreparedSource> PrepareAsync(StorageItemLocator locator, CancellationToken cancellationToken)
	{
		if (locator is ConfiguredItemLocator configured)
		{
			return new PreparedSource(await workspace.ResolveAsync(configured.Reference, cancellationToken).ConfigureAwait(false), null);
		}

		if (locator is not ArchiveRecordLocator record)
		{
			throw new NotSupportedException("Only files and physical archive records can be copied by this transfer handler.");
		}

		var source = workspace.Sources.Single(source => source.SourceId == record.Root.SourceId);
		var archive = await ArchiveModel.OpenAsync(source, record.Container, workspace.ModelFactory, readers, credentials, cancellationToken).ConfigureAwait(false);
		try
		{
			if (!archive.Entries.Any(entry => entry.Id == record.RecordId && !entry.IsDirectory && ArchiveEntryPath.Normalize(entry.Path) == record.EntryPath))
			{
				throw new FileNotFoundException("The selected archive record changed or no longer exists.");
			}

			var reference = new StorableReference(archive.Root.Reference.SourceId, $"entry:{record.RecordId}");

			return new PreparedSource(await archive.ResolveAsync(reference, cancellationToken).ConfigureAwait(false), archive);
		}
		catch
		{
			await archive.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	private static StorageTransferOutcome GetOutcome(IStorageImportFile? staged, StorageTransferPhase phase, long bytes, bool skipped = false)
	{
		var publication = staged?.CommittedItem is not null ? StorageTransferPublication.Committed
			: staged?.PublicationAttempted is true && !skipped ? StorageTransferPublication.Unknown : StorageTransferPublication.None;

		return new StorageTransferOutcome(publication, phase, staged?.CommittedItem, bytes);
	}

	private static async Task WaitWhilePausedAsync(IStorageOperationControl? control, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			while (control?.IsPauseRequested is true)
			{
				control.AcknowledgePauseState(true);
				await Task.Delay(50, cancellationToken).ConfigureAwait(false);
			}
		}
		finally
		{
			control?.AcknowledgePauseState(false);
		}
	}

	private sealed record PreparedSource(IStorableModel Item, ArchiveModel? Archive) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			try
			{
				await Item.DisposeAsync().ConfigureAwait(false);
			}
			finally
			{
				if (Archive is not null)
				{
					await Archive.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
	}
}

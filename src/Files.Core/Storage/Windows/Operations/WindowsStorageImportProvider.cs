// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Storage;
using Files.Core.Storage.Transfers;

namespace Files.Core.Storage.Windows;

internal sealed class WindowsStorageImportProvider(WindowsStorageSource source) : IStorageImportProvider
{
	public bool CanImport(StorableReference destination)
	{
		return destination.SourceId == source.SourceId && destination.LastKnownAddress?.Scheme == WindowsStorageSource.FileAddressScheme;
	}

	public async ValueTask<IStorageImportFile> StageAsync(StorableReference destination, string name, StorageConflictBehavior conflict, CancellationToken cancellationToken)
	{
		WindowsStorageOperationHandler.ValidateName(name);

		if (!CanImport(destination) || await source.ResolveAsync(destination, cancellationToken).ConfigureAwait(false) is not WindowsFolder { FileSystemPath: { } path })
		{
			throw new NotSupportedException("The destination must be a Windows file-system folder.");
		}

		var temporaryPath = Path.Combine(path, $".Files-{Guid.NewGuid():N}.tmp");
		if (conflict == StorageConflictBehavior.Fail && (File.Exists(Path.Combine(path, name)) || Directory.Exists(Path.Combine(path, name))))
		{
			throw new IOException("The destination name already exists.");
		}

		var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
		try
		{
			var address = new StorageAddress(WindowsStorageSource.FileAddressScheme, temporaryPath);
			var item = await source.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
			var reference = new StorableReference(source.SourceId, item.Id, address);

			return new ImportFile(output, reference, destination, name, conflict, source, new WindowsStorageOperationHandler(source));
		}
		catch
		{
			await output.DisposeAsync().ConfigureAwait(false);
			File.Delete(temporaryPath);
			throw;
		}
	}

	private sealed class ImportFile(FileStream output, StorableReference temporary, StorableReference destination, string name, StorageConflictBehavior conflict,
		WindowsStorageSource source, WindowsStorageOperationHandler operations) : IStorageImportFile
	{
		private bool _disposed;
		public Stream Output => output;
		public bool PublicationAttempted { get; private set; }
		public StorableReference? CommittedItem { get; private set; }

		public async ValueTask<StorageOperationResult> CommitAsync(IStorageOperationControl? control, CancellationToken cancellationToken)
		{
			await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			await output.DisposeAsync().ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			PublicationAttempted = true;
			var result = await operations.ExecuteAsync(new MoveOperationRequest(temporary, destination, name, conflict), cancellationToken: cancellationToken, operationControl: control)
				.ConfigureAwait(false);
			if (result.Succeeded && !result.Skipped)
			{
				CommittedItem = result.ResultItem ?? throw new IOException("The Shell did not identify the published destination.");
			}
			else if (result.Skipped)
			{
				PublicationAttempted = false;
			}

			return result;
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			await output.DisposeAsync().ConfigureAwait(false);
			if (CommittedItem is null)
			{
				try
				{
					var remaining = await source.ResolveAsync(temporary.LastKnownAddress!).ConfigureAwait(false);
					if (remaining.Id != temporary.ItemId)
					{
						throw new IOException("Transfer staging was replaced; the replacement will not be deleted.");
					}
				}
				catch (FileNotFoundException)
				{
					return;
				}
				catch (DirectoryNotFoundException)
				{
					return;
				}

				var result = await operations.ExecuteAsync(new DeleteOperationRequest(temporary, permanently: true)).ConfigureAwait(false);
				if (!result.Succeeded && result.Error is not FileNotFoundException and not DirectoryNotFoundException)
				{
					throw new IOException("Transfer staging could not be removed.", result.Error);
				}
			}
		}
	}
}

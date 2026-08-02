// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Executes mutations within one configured FTP source.
/// </summary>
public sealed class FtpStorageOperationHandler :
	IStorageOperationHandler
{
	private const int CopyBufferSize = 81920;
	private const int MaximumGeneratedNameAttempts = 10000;
	private readonly FtpStorageSource _source;

	public FtpStorageOperationHandler(FtpStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	public bool CanHandle(StorageOperationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return request switch
		{
			RenameOperationRequest rename =>
				IsOwned(rename.Item),
			CreateItemOperationRequest create =>
				IsOwned(create.Parent),
			CopyOperationRequest copy =>
				IsOwned(copy.Item)
					&& IsOwned(copy.DestinationFolder),
			MoveOperationRequest move =>
				IsOwned(move.Item)
					&& IsOwned(move.DestinationFolder),
			DeleteOperationRequest delete =>
				IsOwned(delete.Item),
			_ => false,
		};
	}

	public async ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (!CanHandle(request))
		{
			return Failed(new NotSupportedException($"The FTP operation handler cannot handle '{request.GetType().Name}'."));
		}

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			return request switch
			{
				RenameOperationRequest rename =>
					await ExecuteRenameAsync(rename, progress, cancellationToken).ConfigureAwait(false),
				CreateItemOperationRequest create =>
					await ExecuteCreateAsync(create, progress, cancellationToken).ConfigureAwait(false),
				CopyOperationRequest copy =>
					await ExecuteCopyAsync(copy, progress, cancellationToken).ConfigureAwait(false),
				MoveOperationRequest move =>
					await ExecuteMoveAsync(move, progress, cancellationToken).ConfigureAwait(false),
				DeleteOperationRequest delete =>
					await ExecuteDeleteAsync(delete, progress, cancellationToken).ConfigureAwait(false),
				_ => Failed(new NotSupportedException($"The FTP operation handler cannot handle '{request.GetType().Name}'.")),
			};
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Failed(exception);
		}
	}

	private async ValueTask<StorageOperationResult> ExecuteRenameAsync(RenameOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		FtpPath.ValidateName(request.NewName);
		var item = await ResolveAsync(request.Item, cancellationToken).ConfigureAwait(false);
		var parentPath = item.Path.Parent
			?? throw new NotSupportedException("The configured FTP root cannot be renamed.");
		if (!parentPath.IsWithin(_source.Profile.RootPath, _source.Profile.PathComparer))
		{
			throw new NotSupportedException("The configured FTP root cannot be renamed.");
		}

		var destinationPath = parentPath.Combine(request.NewName);
		progress?.Report(new StorageOperationProgress(0, 1, request.Item));

		if (StringComparer.Ordinal.Equals(item.Path.Value, destinationPath.Value))
		{
			var unchanged = _source.CreateReference(item.Path);
			progress?.Report(new StorageOperationProgress(1, 1, unchanged));

			return new StorageOperationResult(true, unchanged);
		}

		var isCaseOnlyRename =
			_source.Profile.PathComparison
				is FtpPathComparison.CaseInsensitive
			&& _source.Profile.PathComparer.Equals(item.Path.Value, destinationPath.Value);
		if (isCaseOnlyRename)
		{
			await ExecuteCaseOnlyRenameAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await EnsureDoesNotExistAsync(destinationPath, cancellationToken).ConfigureAwait(false);
			await MoveAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);
		}

		var result = _source.CreateReference(destinationPath);
		progress?.Report(new StorageOperationProgress(1, 1, result));

		return new StorageOperationResult(true, result);
	}

	private async ValueTask<StorageOperationResult> ExecuteCreateAsync(CreateItemOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		FtpPath.ValidateName(request.Name);
		var parent = await ResolveFolderAsync(request.Parent, cancellationToken).ConfigureAwait(false);
		var destinationPath = await ResolveDestinationPathAsync(parent.Path, request.Name, request.ConflictBehavior, cancellationToken).ConfigureAwait(false);

		progress?.Report(new StorageOperationProgress(0, 1, request.Parent));
		await _source.Connection.ExecuteAsync(session => request.Kind is StorageItemKind.Folder ? session.CreateFolderAsync(destinationPath, cancellationToken) : session.CreateFileAsync(destinationPath, cancellationToken), cancellationToken).ConfigureAwait(false);

		var result = _source.CreateReference(destinationPath);
		progress?.Report(new StorageOperationProgress(1, 1, result));

		return new StorageOperationResult(true, result);
	}

	private async ValueTask<StorageOperationResult> ExecuteCopyAsync(CopyOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var item = await ResolveAsync(request.Item, cancellationToken).ConfigureAwait(false);
		var destinationFolder = await ResolveFolderAsync(request.DestinationFolder, cancellationToken).ConfigureAwait(false);
		var requestedName = request.NewName ?? item.Name;
		FtpPath.ValidateName(requestedName);

		if (item is FtpFolder && destinationFolder.Path.IsWithin(item.Path, _source.Profile.PathComparer))
		{
			throw new IOException("An FTP folder cannot be copied into itself.");
		}

		var destinationPath = await ResolveDestinationPathAsync(destinationFolder.Path, requestedName, request.ConflictBehavior, cancellationToken).ConfigureAwait(false);
		progress?.Report(new StorageOperationProgress(0, 1, request.Item));

		await CopyAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);

		var result = _source.CreateReference(destinationPath);
		progress?.Report(new StorageOperationProgress(1, 1, result));

		return new StorageOperationResult(true, result);
	}

	private async ValueTask<StorageOperationResult> ExecuteMoveAsync(MoveOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var item = await ResolveAsync(request.Item, cancellationToken).ConfigureAwait(false);
		var destinationFolder = await ResolveFolderAsync(request.DestinationFolder, cancellationToken).ConfigureAwait(false);
		var requestedName = request.NewName ?? item.Name;
		FtpPath.ValidateName(requestedName);

		if (item is FtpFolder && destinationFolder.Path.IsWithin(item.Path, _source.Profile.PathComparer))
		{
			throw new IOException("An FTP folder cannot be moved into itself.");
		}

		var desiredPath = destinationFolder.Path.Combine(requestedName);
		progress?.Report(new StorageOperationProgress(0, 1, request.Item));
		if (StringComparer.Ordinal.Equals(item.Path.Value, desiredPath.Value))
		{
			var unchanged = _source.CreateReference(item.Path);
			progress?.Report(new StorageOperationProgress(1, 1, unchanged));

			return new StorageOperationResult(true, unchanged);
		}

		var isCaseOnlyMove =
			_source.Profile.PathComparison
				is FtpPathComparison.CaseInsensitive
			&& _source.Profile.PathComparer.Equals(item.Path.Value, desiredPath.Value);
		FtpPath destinationPath;
		if (isCaseOnlyMove)
		{
			destinationPath = desiredPath;
			await ExecuteCaseOnlyRenameAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			destinationPath = await ResolveDestinationPathAsync(destinationFolder.Path, requestedName, request.ConflictBehavior, cancellationToken).ConfigureAwait(false);
			await MoveAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);
		}

		var result = _source.CreateReference(destinationPath);
		progress?.Report(new StorageOperationProgress(1, 1, result));

		return new StorageOperationResult(true, result);
	}

	private async ValueTask<StorageOperationResult> ExecuteDeleteAsync(DeleteOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		if (!request.Permanently)
		{
			throw new NotSupportedException("FTP has no Recycle Bin. Permanent deletion must be requested explicitly.");
		}

		var item = await ResolveAsync(request.Item, cancellationToken).ConfigureAwait(false);
		if (_source.Profile.PathComparer.Equals(item.Path.Value, _source.Profile.RootPath.Value))
		{
			throw new NotSupportedException("The configured FTP root cannot be deleted.");
		}

		progress?.Report(new StorageOperationProgress(0, 1, request.Item));
		await _source.Connection.ExecuteAsync(session => session.DeleteAsync(item.Path, item.Kind, cancellationToken), cancellationToken).ConfigureAwait(false);
		progress?.Report(new StorageOperationProgress(1, 1));

		return new StorageOperationResult(true, null);
	}

	private async ValueTask ExecuteCaseOnlyRenameAsync(FtpStorable item, FtpPath destinationPath, CancellationToken cancellationToken)
	{
		var parentPath = item.Path.Parent!;
		var temporaryPath = await ResolveDestinationPathAsync(parentPath, $".files-rename-{Guid.NewGuid():N}", StorageConflictBehavior.GenerateUniqueName, cancellationToken).ConfigureAwait(false);

		await MoveAsync(item, temporaryPath, cancellationToken).ConfigureAwait(false);
		try
		{
			await _source.Connection.ExecuteAsync(session => session.MoveAsync(temporaryPath, destinationPath, item.Kind, cancellationToken), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception renameError)
		{
			try
			{
				await _source.Connection.ExecuteAsync(session => session.MoveAsync(temporaryPath, item.Path, item.Kind, CancellationToken.None), CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception rollbackError)
			{
				throw new AggregateException($"The case-only FTP rename and rollback from temporary path '{temporaryPath.Value}' both failed.", renameError, rollbackError);
			}

			throw;
		}
	}

	private async ValueTask CopyAsync(FtpStorable item, FtpPath destinationPath, CancellationToken cancellationToken)
	{
		if (item is FtpFolder folder)
		{
			await CopyFolderAsync(folder, destinationPath, cancellationToken).ConfigureAwait(false);

			return;
		}

		await CopyFileAsync(item, destinationPath, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask CopyFolderAsync(FtpFolder folder, FtpPath destinationPath, CancellationToken cancellationToken)
	{
		var temporaryPath = await CreateTemporaryCopyPathAsync(destinationPath, cancellationToken).ConfigureAwait(false);
		var ownsTemporary = false;

		try
		{
			await _source.Connection.ExecuteAsync(session => session.CreateFolderAsync(temporaryPath, cancellationToken), cancellationToken).ConfigureAwait(false);
			ownsTemporary = true;
			var children = await _source.Resolver.GetItemsAsync(folder.Path, cancellationToken).ConfigureAwait(false);
			foreach (var childEntry in children)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var child = _source.CreateStorable(childEntry);
				await CopyAsync(child, temporaryPath.Combine(childEntry.Name), cancellationToken).ConfigureAwait(false);
			}

			await _source.Connection.ExecuteAsync(session => session.MoveAsync(temporaryPath, destinationPath, FtpEntryKind.Folder, cancellationToken), cancellationToken).ConfigureAwait(false);
			ownsTemporary = false;
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			if (ownsTemporary)
			{
				await TryDeletePartialCopyAsync(temporaryPath).ConfigureAwait(false);
			}

			throw;
		}
		catch (Exception copyError)
		{
			var cleanupError = ownsTemporary
				? await TryDeletePartialCopyAsync(temporaryPath).ConfigureAwait(false)
				: null;
			ThrowIfCleanupFailed(copyError, cleanupError);

			throw;
		}
	}

	private async ValueTask CopyFileAsync(FtpStorable file, FtpPath destinationPath, CancellationToken cancellationToken)
	{
		var temporaryPath = await CreateTemporaryCopyPathAsync(destinationPath, cancellationToken).ConfigureAwait(false);
		var ownsTemporary = false;

		try
		{
			await _source.Connection.ExecuteAsync(session => session.CreateFileAsync(temporaryPath, cancellationToken), cancellationToken).ConfigureAwait(false);
			ownsTemporary = true;
			{
				await using var input = await _source.Connection.OpenReadAsync(file.Path, cancellationToken).ConfigureAwait(false);
				await using var output = await _source.Connection.OpenWriteAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
				await input.CopyToAsync(output, CopyBufferSize, cancellationToken).ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}

			await _source.Connection.ExecuteAsync(session => session.MoveAsync(temporaryPath, destinationPath, FtpEntryKind.File, cancellationToken), cancellationToken).ConfigureAwait(false);
			ownsTemporary = false;
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			if (ownsTemporary)
			{
				await TryDeletePartialCopyAsync(temporaryPath).ConfigureAwait(false);
			}

			throw;
		}
		catch (Exception copyError)
		{
			var cleanupError = ownsTemporary
				? await TryDeletePartialCopyAsync(temporaryPath).ConfigureAwait(false)
				: null;
			ThrowIfCleanupFailed(copyError, cleanupError);

			throw;
		}
	}

	private ValueTask<FtpPath> CreateTemporaryCopyPathAsync(FtpPath destinationPath, CancellationToken cancellationToken)
	{
		var parentPath = destinationPath.Parent
			?? throw new NotSupportedException("An FTP item cannot be copied over the configured root.");

		return ResolveDestinationPathAsync(parentPath, $".files-copy-{Guid.NewGuid():N}", StorageConflictBehavior.GenerateUniqueName, cancellationToken);
	}

	private ValueTask MoveAsync(FtpStorable item, FtpPath destinationPath, CancellationToken cancellationToken)
	{
		return _source.Connection.ExecuteAsync(session => session.MoveAsync(item.Path, destinationPath, item.Kind, cancellationToken), cancellationToken);
	}

	private async ValueTask<FtpPath> ResolveDestinationPathAsync(FtpPath parentPath, string desiredName, StorageConflictBehavior conflictBehavior, CancellationToken cancellationToken)
	{
		var desiredPath = parentPath.Combine(desiredName);
		if (await _source.Resolver .TryResolveAsync(desiredPath, cancellationToken) .ConfigureAwait(false) is null)
		{
			return desiredPath;
		}

		if (conflictBehavior is StorageConflictBehavior.Fail)
		{
			throw new IOException($"An item named '{desiredName}' already exists.");
		}

		var (baseName, extension) = SplitName(desiredName);
		for (var index = 2; index <= MaximumGeneratedNameAttempts; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var candidate = parentPath.Combine($"{baseName} ({index}){extension}");
			if (await _source.Resolver .TryResolveAsync(candidate, cancellationToken) .ConfigureAwait(false) is null)
			{
				return candidate;
			}
		}

		throw new IOException($"A unique name could not be generated for '{desiredName}'.");
	}

	private async ValueTask EnsureDoesNotExistAsync(FtpPath path, CancellationToken cancellationToken)
	{
		if (await _source.Resolver .TryResolveAsync(path, cancellationToken) .ConfigureAwait(false) is not null)
		{
			throw new IOException($"An item named '{path.Name}' already exists.");
		}
	}

	private async ValueTask<FtpStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken)
	{
		var item = await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		return item as FtpStorable
			?? throw new NotSupportedException("The operation target is not an FTP item.");
	}

	private async ValueTask<FtpFolder> ResolveFolderAsync(StorableReference reference, CancellationToken cancellationToken)
	{
		var item = await ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		return item as FtpFolder
			?? throw new NotSupportedException("The FTP operation destination must be a folder.");
	}

	private async ValueTask<Exception?> TryDeletePartialCopyAsync(FtpPath path)
	{
		try
		{
			var entry = await _source.Resolver.TryResolveAsync(path, CancellationToken.None).ConfigureAwait(false);
			if (entry is not null)
			{
				await _source.Connection.ExecuteAsync(session => session.DeleteAsync(path, entry.Kind, CancellationToken.None), CancellationToken.None).ConfigureAwait(false);
			}

			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}

	private bool IsOwned(StorableReference reference)
	{
		return reference.SourceId == _source.SourceId;
	}

	private static (string BaseName, string Extension) SplitName(string name)
	{
		var extensionIndex = name.LastIndexOf('.');

		return extensionIndex > 0
			? (name[..extensionIndex], name[extensionIndex..])
			: (name, string.Empty);
	}

	private static StorageOperationResult Failed(Exception exception)
	{
		return new StorageOperationResult(false, null, exception);
	}

	private static void ThrowIfCleanupFailed(Exception copyError, Exception? cleanupError)
	{
		if (cleanupError is not null)
		{
			throw new AggregateException("FTP copy and cleanup both failed.", copyError, cleanupError);
		}
	}
}

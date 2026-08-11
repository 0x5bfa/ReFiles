// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Executes Windows Shell storage operations.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsStorageOperationHandler : IStorageOperationHandler
{
	private const FILEOPERATION_FLAGS RecycleOnDeleteFlag = FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE;

	private readonly WindowsStorageSource _source;

	/// <summary>Initializes a Windows Shell operation handler.</summary>
	/// <param name="source">The Windows storage source.</param>
	public WindowsStorageOperationHandler(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>Determines whether this handler owns a request.</summary>
	/// <param name="request">The operation request.</param>
	/// <returns><see langword="true"/> when the request targets Windows storage.</returns>
	public bool CanHandle(StorageOperationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return request switch
		{
			RenameOperationRequest rename => IsOwnedFileSystemItem(rename.Item),
			CreateItemOperationRequest create => IsOwnedFileSystemItem(create.Parent),
			CopyOperationRequest copy => IsOwnedFileSystemItem(copy.Item) && IsOwnedFileSystemItem(copy.DestinationFolder),
			MoveOperationRequest move => IsOwnedFileSystemItem(move.Item) && IsOwnedFileSystemItem(move.DestinationFolder),
			DeleteOperationRequest delete => IsOwned(delete.Item),
			_ => false,
		};
	}

	/// <summary>Executes a Windows Shell storage operation.</summary>
	/// <param name="request">The operation request.</param>
	/// <param name="progress">The optional progress receiver.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The operation result.</returns>
	[SupportedOSPlatform("windows6.0.6000")]
	public async ValueTask<StorageOperationResult> ExecuteAsync(StorageOperationRequest request, IProgress<StorageOperationProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (!CanHandle(request))
		{
			return Failed(new NotSupportedException($"The Windows storage handler cannot handle '{request.GetType().Name}'."));
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
					await ExecuteTransferAsync(copy.Item, copy.DestinationFolder, copy.NewName, copy.ConflictBehavior, move: false, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false),
				MoveOperationRequest move =>
					await ExecuteTransferAsync(move.Item, move.DestinationFolder, move.NewName, move.ConflictBehavior, move: true, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false),
				DeleteOperationRequest delete =>
					await ExecuteDeleteAsync(delete, progress, cancellationToken).ConfigureAwait(false),
				_ => Failed(new NotSupportedException($"The Windows storage handler cannot handle '{request.GetType().Name}'.")),
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
		ValidateName(request.NewName);

		var item = await ResolveFileSystemItemAsync(request.Item, "rename", cancellationToken).ConfigureAwait(false);
		var itemPath = item.FileSystemPath!;
		var parentPath = Path.GetDirectoryName(itemPath);
		if (string.IsNullOrWhiteSpace(parentPath))
		{
			return Failed(new IOException("The item does not have a resolvable parent directory."));
		}

		var destinationPath = Path.Combine(parentPath, request.NewName);
		var hasSamePathSpelling = PathSpellingEquals(itemPath, destinationPath);
		var isSameItem = hasSamePathSpelling
			|| PathEquals(itemPath, destinationPath)
				&& await IsSameFileSystemItemAsync(destinationPath, item.Id, cancellationToken).ConfigureAwait(false);
		if (!hasSamePathSpelling && PathExists(destinationPath) && !isSameItem)
		{
			return Failed(new IOException($"An item named '{request.NewName}' already exists."));
		}

		progress?.Report(new StorageOperationProgress(0, 1, request.Item));
		if (!hasSamePathSpelling)
		{
			var outcome = await _source.ShellItemResolver.InvokeOperationAsync(item.ParsingName, shellItem => ExecuteRename(shellItem, item.Id, request.NewName), cancellationToken).ConfigureAwait(false);
			if (!outcome.Succeeded)
			{
				return Failed(outcome.Error!);
			}
		}

		var expectedResultItemId = _source.IsFileSystemIdentity(item.Id) ? item.Id : null;
		var resultItem = await ResolveResultAsync(destinationPath, expectedResultItemId).ConfigureAwait(false);
		progress?.Report(new StorageOperationProgress(1, 1, resultItem));

		return new StorageOperationResult(true, resultItem);
	}

	private async ValueTask<StorageOperationResult> ExecuteCreateAsync(CreateItemOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		ValidateName(request.Name);
		var parent = await ResolveFileSystemFolderAsync(request.Parent, "create an item", cancellationToken).ConfigureAwait(false);
		var parentPath = parent.FileSystemPath!;
		var destinationName = ResolveDestinationName(parentPath, request.Name, request.Kind is StorageItemKind.Folder, request.ConflictBehavior);
		var destinationPath = Path.Combine(parentPath, destinationName);

		progress?.Report(new StorageOperationProgress(0, 1, request.Parent));
		var outcome = await _source.ShellItemResolver.InvokeOperationAsync(parent.ParsingName, destinationFolder => ExecuteCreate(destinationFolder, destinationName, request.Kind), cancellationToken)
			.ConfigureAwait(false);
		if (!outcome.Succeeded)
		{
			return Failed(outcome.Error!);
		}

		var resultItem = await ResolveResultAsync(destinationPath).ConfigureAwait(false);
		progress?.Report(new StorageOperationProgress(1, 1, resultItem));

		return new StorageOperationResult(true, resultItem);
	}

	private async ValueTask<StorageOperationResult> ExecuteTransferAsync(StorableReference itemReference, StorableReference destinationFolderReference, string? requestedName, StorageConflictBehavior conflictBehavior, bool move, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var operationName = move ? "move" : "copy";
		var item = await ResolveFileSystemItemAsync(itemReference, operationName, cancellationToken).ConfigureAwait(false);
		var destinationFolder = await ResolveFileSystemFolderAsync(destinationFolderReference, operationName, cancellationToken).ConfigureAwait(false);
		var itemPath = item.FileSystemPath!;
		var destinationFolderPath = destinationFolder.FileSystemPath!;

		var originalName = Path.GetFileName(itemPath);
		if (string.IsNullOrWhiteSpace(originalName))
		{
			return Failed(new IOException("The source item does not have a valid file-system name."));
		}

		var desiredName = requestedName ?? originalName;
		ValidateName(desiredName);
		var desiredPath = Path.Combine(destinationFolderPath, desiredName);
		var ignoredExistingPath = move
			&& PathEquals(itemPath, desiredPath)
			&& await IsSameFileSystemItemAsync(desiredPath, item.Id, cancellationToken).ConfigureAwait(false)
				? itemPath
				: null;
		var destinationName = ResolveDestinationName(destinationFolderPath, desiredName, item is WindowsFolder, conflictBehavior, ignoredExistingPath);
		var destinationPath = Path.Combine(destinationFolderPath, destinationName);

		progress?.Report(new StorageOperationProgress(0, 1, itemReference));
		if (move && PathSpellingEquals(itemPath, destinationPath))
		{
			var unchanged = new StorableReference(_source.SourceId, item.Id, item.Address);
			progress?.Report(new StorageOperationProgress(1, 1, unchanged));

			return new StorageOperationResult(true, unchanged);
		}

		var outcome = await _source.ShellItemResolver
			.InvokeOperationAsync(item.ParsingName, destinationFolder.ParsingName, (sourceItem, destinationItem) => ExecuteTransfer(sourceItem, destinationItem, destinationName, move), cancellationToken)
			.ConfigureAwait(false);
		if (!outcome.Succeeded)
		{
			return Failed(outcome.Error!);
		}

		var resultItem = await ResolveResultAsync(destinationPath).ConfigureAwait(false);
		progress?.Report(new StorageOperationProgress(1, 1, resultItem));

		return new StorageOperationResult(true, resultItem);
	}

	private async ValueTask<StorageOperationResult> ExecuteDeleteAsync(DeleteOperationRequest request, IProgress<StorageOperationProgress>? progress, CancellationToken cancellationToken)
	{
		var resolved = await _source.ResolveAsync(request.Item, cancellationToken).ConfigureAwait(false);
		if (resolved is not WindowsStorable item)
		{
			return Failed(new NotSupportedException("The delete target is not a Windows Shell item."));
		}

		progress?.Report(new StorageOperationProgress(0, 1, request.Item));
		var outcome = await _source.ShellItemResolver.InvokeOperationAsync(item.ParsingName, shellItem => ExecuteDelete(shellItem, request.Permanently), cancellationToken).ConfigureAwait(false);
		if (!outcome.Succeeded)
		{
			return Failed(outcome.Error!);
		}

		progress?.Report(new StorageOperationProgress(1, 1));

		return new StorageOperationResult(true, null);
	}

	private async ValueTask<WindowsStorable> ResolveFileSystemItemAsync(StorableReference reference, string operationName, CancellationToken cancellationToken)
	{
		var resolved = await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		if (resolved is not WindowsStorable item || item.FileSystemPath is null)
		{
			throw new NotSupportedException($"The Windows storage handler can only {operationName} file-system items.");
		}

		return item;
	}

	private async ValueTask<WindowsFolder> ResolveFileSystemFolderAsync(StorableReference reference, string operationName, CancellationToken cancellationToken)
	{
		var resolved = await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		if (resolved is not WindowsFolder folder || folder.FileSystemPath is null)
		{
			throw new NotSupportedException($"The destination for {operationName} must be a file-system folder.");
		}

		return folder;
	}

	private async ValueTask<StorableReference> ResolveResultAsync(string path, string? expectedItemId = null)
	{
		var resolved = await _source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path), CancellationToken.None).ConfigureAwait(false);
		if (resolved is not IWindowsStorable windowsItem)
		{
			throw new InvalidOperationException("The Windows Shell operation result could not be materialized.");
		}

		if (expectedItemId is not null && !StringComparer.Ordinal.Equals(expectedItemId, windowsItem.Id))
		{
			throw new IOException("The Windows Shell operation affected an unexpected item.");
		}

		return new StorableReference(_source.SourceId, windowsItem.Id, windowsItem.Address);
	}

	private async ValueTask<bool> IsSameFileSystemItemAsync(string path, string expectedItemId, CancellationToken cancellationToken)
	{
		if (!PathExists(path))
		{
			return false;
		}

		try
		{
			var candidate = await _source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path), cancellationToken).ConfigureAwait(false);

			return candidate is IWindowsStorable windowsItem
				&& StringComparer.Ordinal.Equals(expectedItemId, windowsItem.Id);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}

	[SupportedOSPlatform("windows6.0.6000")]
	private static ShellOperationOutcome ExecuteRename(IShellItem shellItem, string expectedItemId, string newName)
	{
		var currentDescriptor = ShellItemHelpers.CreateDescriptor(shellItem, new WindowsItemIdReader());
		if (!StringComparer.Ordinal.Equals(currentDescriptor.ItemId, expectedItemId))
		{
			return new ShellOperationOutcome(false, new IOException("The Windows Shell rename target no longer identifies the requested item."));
		}

		var createResult = PInvoke.CoCreateInstance(typeof(FileOperation).GUID, null, CLSCTX.CLSCTX_LOCAL_SERVER, out IFileOperation? fileOperation);
		if (createResult.Failed || fileOperation is null)
		{
			return Failure(createResult, "The Windows Shell file operation could not be created.");
		}

		var result = ConfigureOperation(fileOperation, allowUndo: true, recycleOnDelete: false);
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell file operation could not be configured.");
		}

		result = fileOperation.RenameItem(shellItem, newName, null);
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell rename could not be queued.");
		}

		result = fileOperation.PerformOperations();
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell rename failed.");
		}

		result = fileOperation.GetAnyOperationsAborted(out var aborted);
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell rename completion could not be read.");
		}

		return aborted
			? new ShellOperationOutcome(false, new OperationCanceledException("The Windows Shell rename was aborted."))
			: new ShellOperationOutcome(true, null);
	}

	[SupportedOSPlatform("windows6.0.6000")]
	private static ShellOperationOutcome ExecuteCreate(IShellItem destinationFolder, string name, StorageItemKind kind)
	{
		var creation = CreateOperation(allowUndo: true);
		if (!creation.Outcome.Succeeded)
		{
			return creation.Outcome;
		}

		var fileOperation = creation.Operation!;
		var attributes = kind switch
		{
			StorageItemKind.File => FileAttributes.Normal,
			StorageItemKind.Folder => FileAttributes.Directory,
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
		var result = fileOperation.NewItem(destinationFolder, (uint)attributes, name, null, null);
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell create operation could not be queued.");
		}

		return Perform(fileOperation, "create");
	}

	[SupportedOSPlatform("windows6.0.6000")]
	private static ShellOperationOutcome ExecuteTransfer(IShellItem item, IShellItem destinationFolder, string destinationName, bool move)
	{
		var creation = CreateOperation(allowUndo: true);
		if (!creation.Outcome.Succeeded)
		{
			return creation.Outcome;
		}

		var fileOperation = creation.Operation!;
		var result = move
			? fileOperation.MoveItem(item, destinationFolder, destinationName, null)
			: fileOperation.CopyItem(item, destinationFolder, destinationName, null);
		if (result.Failed)
		{
			return Failure(result, $"The Windows Shell {(move ? "move" : "copy")} operation could not be queued.");
		}

		return Perform(fileOperation, move ? "move" : "copy");
	}

	[SupportedOSPlatform("windows6.0.6000")]
	private static ShellOperationOutcome ExecuteDelete(IShellItem item, bool permanently)
	{
		var creation = CreateOperation(allowUndo: !permanently, recycleOnDelete: !permanently);
		if (!creation.Outcome.Succeeded)
		{
			return creation.Outcome;
		}

		var fileOperation = creation.Operation!;
		var result = fileOperation.DeleteItem(item, null);
		if (result.Failed)
		{
			return Failure(result, "The Windows Shell delete operation could not be queued.");
		}

		return Perform(fileOperation, "delete");
	}

	[SupportedOSPlatform("windows6.0.6000")]
	private static FileOperationCreation CreateOperation(bool allowUndo, bool recycleOnDelete = false)
	{
		var result = PInvoke.CoCreateInstance(typeof(FileOperation).GUID, null, CLSCTX.CLSCTX_LOCAL_SERVER, out IFileOperation? fileOperation);
		if (result.Failed || fileOperation is null)
		{
			return new FileOperationCreation(null, Failure(result, "The Windows Shell file operation could not be created."));
		}

		result = ConfigureOperation(fileOperation, allowUndo, recycleOnDelete);

		return result.Failed
			? new FileOperationCreation(null, Failure(result, "The Windows Shell file operation could not be configured."))
			: new FileOperationCreation(fileOperation, new ShellOperationOutcome(true, null));
	}

	private static global::Windows.Win32.Foundation.HRESULT ConfigureOperation(IFileOperation fileOperation, bool allowUndo, bool recycleOnDelete)
	{
		var flags = FILEOPERATION_FLAGS.FOF_SILENT
			| FILEOPERATION_FLAGS.FOF_NOCONFIRMATION
			| FILEOPERATION_FLAGS.FOF_NOCONFIRMMKDIR
			| FILEOPERATION_FLAGS.FOF_NOERRORUI;
		if (allowUndo)
		{
			flags |= FILEOPERATION_FLAGS.FOF_ALLOWUNDO;
		}

		if (recycleOnDelete)
		{
			flags |= RecycleOnDeleteFlag;
		}

		return fileOperation.SetOperationFlags(flags);
	}

	private static ShellOperationOutcome Perform(IFileOperation fileOperation, string operationName)
	{
		var result = fileOperation.PerformOperations();
		if (result.Failed)
		{
			return Failure(result, $"The Windows Shell {operationName} operation failed.");
		}

		result = fileOperation.GetAnyOperationsAborted(out var aborted);
		if (result.Failed)
		{
			return Failure(result, $"The Windows Shell {operationName} completion could not be read.");
		}

		return aborted
			? new ShellOperationOutcome(false, new OperationCanceledException($"The Windows Shell {operationName} operation was aborted."))
			: new ShellOperationOutcome(true, null);
	}

	private static string ResolveDestinationName(string destinationFolderPath, string desiredName, bool isFolder, StorageConflictBehavior conflictBehavior, string? ignoredExistingPath = null)
	{
		ValidateName(desiredName);

		var desiredPath = Path.Combine(destinationFolderPath, desiredName);
		if (!PathExists(desiredPath) || ignoredExistingPath is not null && PathEquals(desiredPath, ignoredExistingPath))
		{
			return desiredName;
		}

		if (conflictBehavior is StorageConflictBehavior.Fail)
		{
			throw new IOException($"An item named '{desiredName}' already exists.");
		}

		if (conflictBehavior is not StorageConflictBehavior.GenerateUniqueName)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}

		var extension = isFolder ? string.Empty : Path.GetExtension(desiredName);
		var stem = extension.Length is 0
			? desiredName
			: desiredName[..^extension.Length];
		for (var suffix = 2; suffix < int.MaxValue; suffix++)
		{
			var candidate = $"{stem} ({suffix}){extension}";
			if (!PathExists(Path.Combine(destinationFolderPath, candidate)))
			{
				return candidate;
			}
		}

		throw new IOException($"A unique destination name could not be generated for '{desiredName}'.");
	}

	private static bool PathExists(string path)
	{
		return File.Exists(path) || Directory.Exists(path);
	}

	private static bool PathEquals(string first, string second)
	{
		return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(first), Path.GetFullPath(second));
	}

	private static bool PathSpellingEquals(string first, string second)
	{
		return StringComparer.Ordinal.Equals(Path.GetFullPath(first), Path.GetFullPath(second));
	}

	private bool IsOwned(StorableReference reference)
	{
		return reference.SourceId == _source.SourceId;
	}

	private bool IsOwnedFileSystemItem(StorableReference reference)
	{
		return IsOwned(reference)
			&& reference.LastKnownAddress is { } address
			&& address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase);
	}

	private static void ValidateName(string newName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		if (newName is "." or ".."
			|| newName.Length > 255
			|| newName.EndsWith(' ')
			|| newName.EndsWith('.')
			|| newName.Contains(Path.DirectorySeparatorChar)
			|| newName.Contains(Path.AltDirectorySeparatorChar)
			|| newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
			|| IsReservedDosDeviceName(newName))
		{
			throw new ArgumentException("The new name must be a single valid Windows file-system name.", nameof(newName));
		}
	}

	private static bool IsReservedDosDeviceName(string newName)
	{
		var extensionIndex = newName.IndexOf('.');
		var stem = extensionIndex < 0
			? newName
			: newName[..extensionIndex];

		return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
			|| stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
			|| stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
			|| stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
			|| IsNumberedDosDeviceName(stem, "COM")
			|| IsNumberedDosDeviceName(stem, "LPT");
	}

	private static bool IsNumberedDosDeviceName(string candidate, string prefix)
	{
		return candidate.Length is 4
			&& candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& candidate[3] is >= '1' and <= '9';
	}

	private static ShellOperationOutcome Failure(global::Windows.Win32.Foundation.HRESULT result, string message)
	{
		return new ShellOperationOutcome(false, new IOException($"{message} HRESULT={result}."));
	}

	private static StorageOperationResult Failed(Exception exception)
	{
		return new StorageOperationResult(false, null, exception);
	}

	private sealed record ShellOperationOutcome(bool Succeeded, Exception? Error);

	private sealed record FileOperationCreation(IFileOperation? Operation, ShellOperationOutcome Outcome);
}

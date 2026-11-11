// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Files.Settings;

internal sealed class LocalSettingsStorage : ISettingsStorage
{
	private readonly IStorageReader _workspace;
	private readonly IStorageOperationService _operations;
	private readonly string _settingsDirectory;

	internal LocalSettingsStorage(IStorageReader workspace, IStorageOperationService operations, string localFolderPath)
	{
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(operations);
		ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

		_workspace = workspace;
		_operations = operations;
		_settingsDirectory = Path.Combine(Path.GetFullPath(localFolderPath), "Settings");
	}

	public async Task<string?> ReadTextAsync(string fileName, CancellationToken cancellationToken = default)
	{
		var path = GetPath(fileName);
		if (!PathExists(path))
		{
			return null;
		}

		await using var model = await ResolveFileAsync(path, cancellationToken).ConfigureAwait(false);
		var content = model.Get<IReadableContent>() ?? throw new NotSupportedException("The settings file does not provide readable content.");
		await using var stream = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

		return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task WriteTextAsync(string fileName, string text, CancellationToken cancellationToken = default)
	{
		var destination = GetPath(fileName);
		ArgumentNullException.ThrowIfNull(text);
		cancellationToken.ThrowIfCancellationRequested();

		await using var directory = await EnsureSettingsDirectoryAsync(cancellationToken).ConfigureAwait(false);
		var temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
		var temporaryPath = Path.Combine(_settingsDirectory, temporaryName);
		StorableReference? temporaryReference = null;
		try
		{
			var createResult = await _operations.ExecuteAndWaitAsync(new StorageOperationRequest[] { new CreateItemOperationRequest(directory.Reference, temporaryName, StorageItemKind.File) }, cancellationToken)
				.ConfigureAwait(false);
			if (!createResult.Succeeded || createResult.ResultItem is null)
			{
				throw createResult.Error ?? new IOException("The temporary settings document could not be created.");
			}

			temporaryReference = createResult.ResultItem;
			await WriteTemporaryAsync(temporaryReference, text, cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			if (PathExists(destination))
			{
				ReplaceFile(destination, temporaryPath);
				temporaryReference = null;

				return;
			}

			var moveResult = await _operations.ExecuteAndWaitAsync(new StorageOperationRequest[] { new MoveOperationRequest(temporaryReference, directory.Reference, fileName) }, cancellationToken)
				.ConfigureAwait(false);
			if (moveResult.Succeeded)
			{
				temporaryReference = null;

				return;
			}

			if (PathExists(destination))
			{
				ReplaceFile(destination, temporaryPath);
				temporaryReference = null;

				return;
			}

			throw moveResult.Error ?? new IOException("The settings document could not be committed.");
		}
		finally
		{
			if (temporaryReference is not null)
			{
				await TryDeleteTemporaryAsync(temporaryReference).ConfigureAwait(false);
			}
		}
	}

	private async ValueTask<IFolderModel> EnsureSettingsDirectoryAsync(CancellationToken cancellationToken)
	{
		if (!PathExists(_settingsDirectory))
		{
			var parentPath = Path.GetDirectoryName(_settingsDirectory) ?? throw new IOException("The settings directory has no parent.");
			await using var parent = await ResolveFolderAsync(parentPath, cancellationToken).ConfigureAwait(false);
			var createResult = await _operations.ExecuteAndWaitAsync(
				new StorageOperationRequest[] { new CreateItemOperationRequest(parent.Reference, Path.GetFileName(_settingsDirectory), StorageItemKind.Folder) }, cancellationToken).ConfigureAwait(false);
			if (!createResult.Succeeded && !PathExists(_settingsDirectory))
			{
				throw createResult.Error ?? new IOException("The settings directory could not be created.");
			}
		}

		return await ResolveFolderAsync(_settingsDirectory, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<IFolderModel> ResolveFolderAsync(string path, CancellationToken cancellationToken)
	{
		var model = await _workspace.ResolveAsync(CreateAddress(path), cancellationToken).ConfigureAwait(false);
		if (model is IFolderModel folder)
		{
			return folder;
		}

		await model.DisposeAsync().ConfigureAwait(false);
		throw new IOException("The settings path does not identify a folder.");
	}

	private async ValueTask<IFileModel> ResolveFileAsync(string path, CancellationToken cancellationToken)
	{
		var model = await _workspace.ResolveAsync(CreateAddress(path), cancellationToken).ConfigureAwait(false);
		if (model is IFileModel file)
		{
			return file;
		}

		await model.DisposeAsync().ConfigureAwait(false);
		throw new IOException("The settings path does not identify a file.");
	}

	private async Task WriteTemporaryAsync(StorableReference reference, string text, CancellationToken cancellationToken)
	{
		await using var model = await _workspace.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
		var content = model.Get<IWritableContent>() ?? throw new NotSupportedException("The temporary settings document does not provide writable content.");
		await using var stream = await content.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
		await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
		{
			await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
			await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
		}

		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask TryDeleteTemporaryAsync(StorableReference reference)
	{
		try
		{
			var result = await _operations.ExecuteAndWaitAsync(new StorageOperationRequest[] { new DeleteOperationRequest(reference, permanently: true) }, CancellationToken.None).ConfigureAwait(false);
			if (!result.Succeeded)
			{
				Debug.WriteLine($"Failed to delete temporary settings document: {result.Error}");
			}
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Failed to delete temporary settings document: {exception}");
		}
	}

	private static StorageAddress CreateAddress(string path)
	{
		return new StorageAddress(WindowsStorageIdentity.FileAddressScheme, path);
	}

	private static bool PathExists(string path)
	{
		var attributes = PInvoke.GetFileAttributes(path);
		if (attributes != PInvoke.INVALID_FILE_ATTRIBUTES)
		{
			return true;
		}

		var error = Marshal.GetLastPInvokeError();
		if (error is (int)WIN32_ERROR.ERROR_FILE_NOT_FOUND or (int)WIN32_ERROR.ERROR_PATH_NOT_FOUND)
		{
			return false;
		}

		throw new Win32Exception(error, "The settings path could not be inspected.");
	}

	private static void ReplaceFile(string destination, string temporary)
	{
		if (!PInvoke.ReplaceFile(destination, temporary, null, REPLACE_FILE_FLAGS.REPLACEFILE_WRITE_THROUGH))
		{
			var error = Marshal.GetLastPInvokeError();

			throw new Win32Exception(error, "The settings document could not be replaced.");
		}
	}

	private string GetPath(string fileName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		if (fileName != Path.GetFileName(fileName) || fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			throw new ArgumentException("Settings documents require a filename without path components.", nameof(fileName));
		}

		return Path.Combine(_settingsDirectory, fileName);
	}
}

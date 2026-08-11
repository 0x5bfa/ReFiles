// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.IO;
using FluentFTP;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Adapts one FluentFTP control connection to the Core transport contract.
/// </summary>
public sealed class FluentFtpSession : IFtpSession
{
	private readonly AsyncFtpClient _client;
	private readonly StringComparer _pathComparer;
	private int _isDisposed;

	internal FluentFtpSession(AsyncFtpClient client, StringComparer pathComparer)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(pathComparer);

		_client = client;
		_pathComparer = pathComparer;
	}

	/// <summary>Gets metadata for an FTP path.</summary>
	/// <param name="path">The path to inspect.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The entry metadata, or <see langword="null"/> when the path does not exist.</returns>
	public async ValueTask<FtpEntryInfo?> GetEntryAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			var item = await _client.GetObjectInfo(path.Value, dateModified: false, token: cancellationToken).ConfigureAwait(false);
			if (item is not null)
			{
				return CreateEntry(item, path.Parent);
			}
		}
		catch (InvalidOperationException)
		{
			// MLST is optional. Fall back to one parent listing.
		}

		var parent = path.Parent;
		if (parent is null)
		{
			return null;
		}

		var listing = await GetListingAsync(parent, cancellationToken).ConfigureAwait(false);

		return listing.FirstOrDefault(candidate => _pathComparer.Equals(candidate.Path.Value, path.Value));
	}

	/// <summary>Gets the entries in an FTP folder.</summary>
	/// <param name="path">The folder path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The folder entries.</returns>
	public async ValueTask<IReadOnlyList<FtpEntryInfo>> GetListingAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		var items = await _client.GetListing(path.Value, cancellationToken).ConfigureAwait(false);
		var entries = items.Select(item => TryCreateEntry(item, path)).Where(static item => item is not null).Cast<FtpEntryInfo>().ToArray();

		return new ReadOnlyCollection<FtpEntryInfo>(entries);
	}

	/// <summary>Opens an FTP path for reading.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A readable stream.</returns>
	public async ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		return await _client.OpenRead(path.Value, FtpDataType.Binary, restart: 0, checkIfFileExists: true, token: cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Opens an FTP path for writing.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A writable stream.</returns>
	public async ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		return await _client.OpenWrite(path.Value, FtpDataType.Binary, checkIfFileExists: true, token: cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Completes the current transfer.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public async ValueTask CompleteTransferAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		var reply = await _client.GetReply(cancellationToken).ConfigureAwait(false);
		if (!reply.Success)
		{
			throw new IOException($"The FTP data transfer failed with reply code '{reply.Code}'.");
		}
	}

	/// <summary>Creates an empty file.</summary>
	/// <param name="path">The file path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public async ValueTask CreateFileAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		var status = await _client.UploadBytes([], path.Value, FtpRemoteExists.Skip, createRemoteDir: false, progress: null, token: cancellationToken).ConfigureAwait(false);
		if (status is not FtpStatus.Success)
		{
			throw new IOException($"The FTP server did not create '{path.Value}'.");
		}
	}

	/// <summary>Creates a folder.</summary>
	/// <param name="path">The folder path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public async ValueTask CreateFolderAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		var created = await _client.CreateDirectory(path.Value, force: false, token: cancellationToken).ConfigureAwait(false);
		if (!created)
		{
			throw new IOException($"The FTP server did not create '{path.Value}'.");
		}
	}

	/// <summary>Deletes an FTP entry.</summary>
	/// <param name="path">The entry path.</param>
	/// <param name="kind">The entry kind.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public async ValueTask DeleteAsync(FtpPath path, FtpEntryKind kind, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(path);

		if (kind is FtpEntryKind.Folder)
		{
			await _client.DeleteDirectory(path.Value, cancellationToken).ConfigureAwait(false);

			return;
		}

		await _client.DeleteFile(path.Value, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Moves an FTP entry.</summary>
	/// <param name="sourcePath">The source path.</param>
	/// <param name="destinationPath">The destination path.</param>
	/// <param name="kind">The entry kind.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	public async ValueTask MoveAsync(FtpPath sourcePath, FtpPath destinationPath, FtpEntryKind kind, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		var moved = kind is FtpEntryKind.Folder
			? await _client.MoveDirectory(sourcePath.Value, destinationPath.Value, FtpRemoteExists.Skip, cancellationToken).ConfigureAwait(false)
			: await _client.MoveFile(sourcePath.Value, destinationPath.Value, FtpRemoteExists.Skip, cancellationToken).ConfigureAwait(false);
		if (!moved)
		{
			throw new IOException($"The FTP server did not move '{sourcePath.Value}'.");
		}
	}

	/// <summary>Disposes the FTP connection.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		await _client.DisposeAsync().ConfigureAwait(false);
		GC.SuppressFinalize(this);
	}

	private static FtpEntryInfo? TryCreateEntry(FtpListItem item, FtpPath parentPath)
	{
		if (string.IsNullOrWhiteSpace(item.Name) || item.Name is "." or "..")
		{
			return null;
		}

		try
		{
			return CreateEntry(item, parentPath);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static FtpEntryInfo CreateEntry(FtpListItem item, FtpPath? parentPath)
	{
		var fullName = item.FullName?.Replace('\\', '/');
		var path = string.IsNullOrWhiteSpace(fullName)
			? parentPath?.Combine(item.Name)
				?? FtpPath.Parse(item.Name)
			: fullName.StartsWith('/')
				? FtpPath.Parse(fullName)
				: FtpPath.Parse($"{parentPath?.Value ?? string.Empty}/{fullName}");
		var kind = item.Type switch
		{
			FtpObjectType.Directory => FtpEntryKind.Folder,
			FtpObjectType.Link => FtpEntryKind.SymbolicLink,
			_ => FtpEntryKind.File,
		};
		long? size = kind is FtpEntryKind.Folder || item.Size < 0
			? null
			: item.Size;

		return new FtpEntryInfo(path, item.Name, kind, size, ToDateTimeOffset(item.Modified), ToDateTimeOffset(item.Created), item.LinkTarget);
	}

	private static DateTimeOffset? ToDateTimeOffset(DateTime value)
	{
		if (value == DateTime.MinValue)
		{
			return null;
		}

		return value.Kind switch
		{
			DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
			DateTimeKind.Local => new DateTimeOffset(value),
			_ => new DateTimeOffset(value, TimeSpan.Zero),
		};
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	}
}

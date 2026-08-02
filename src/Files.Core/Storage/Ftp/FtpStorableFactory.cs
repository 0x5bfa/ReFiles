// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Creates immutable FTP CoreModels from source-neutral entry snapshots.
/// </summary>
internal sealed class FtpStorableFactory
{
	private readonly FtpStorageSource _source;
	private readonly FtpItemResolver _resolver;
	private readonly FtpConnection _connection;

	public FtpStorableFactory(FtpStorageSource source, FtpItemResolver resolver, FtpConnection connection)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(connection);

		_source = source;
		_resolver = resolver;
		_connection = connection;
	}

	public async ValueTask<FtpStorable> ResolveAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		var entry = await _resolver.ResolveAsync(path, cancellationToken).ConfigureAwait(false);

		return Create(entry);
	}

	public async ValueTask<IReadOnlyList<FtpEntryInfo>> GetItemsAsync(FtpPath folderPath, CancellationToken cancellationToken = default)
	{
		return await _resolver.GetItemsAsync(folderPath, cancellationToken).ConfigureAwait(false);
	}

	public ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		return _connection.OpenReadAsync(path, cancellationToken);
	}

	public ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		return _connection.OpenWriteAsync(path, cancellationToken);
	}

	public FtpStorable Create(FtpEntryInfo entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		var snapshot = FtpStorableSnapshot.FromEntry(entry);

		return entry.Kind is FtpEntryKind.Folder
			? new FtpFolder(_source, snapshot, this)
			: new FtpFile(_source, snapshot, this);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.IO;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Resolves remote paths without retaining a live FTP session.
/// </summary>
internal sealed class FtpItemResolver
{
	private readonly FtpConnectionProfile _profile;
	private readonly FtpConnection _connection;

	public FtpItemResolver(FtpConnectionProfile profile, FtpConnection connection)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(connection);

		_profile = profile;
		_connection = connection;
	}

	public async ValueTask<FtpEntryInfo> ResolveAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		EnsureWithinRoot(path);

		if (_profile.PathComparer.Equals(path.Value, _profile.RootPath.Value))
		{
			return CreateRootEntry();
		}

		var entry = await TryResolveAsync(path, cancellationToken).ConfigureAwait(false);

		return entry ?? throw new FileNotFoundException("The FTP item could not be resolved.", path.Value);
	}

	public async ValueTask<FtpEntryInfo?> TryResolveAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		EnsureWithinRoot(path);

		if (_profile.PathComparer.Equals(path.Value, _profile.RootPath.Value))
		{
			return CreateRootEntry();
		}

		return await _connection.ExecuteAsync(session => session.GetEntryAsync(path, cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyList<FtpEntryInfo>> GetItemsAsync(FtpPath folderPath, CancellationToken cancellationToken = default)
	{
		EnsureWithinRoot(folderPath);
		var items = await _connection.ExecuteAsync(session => session.GetListingAsync(folderPath, cancellationToken), cancellationToken).ConfigureAwait(false);

		var safeItems = items.Where(item => item.Path.IsWithin(_profile.RootPath, _profile.PathComparer)).ToArray();

		return new ReadOnlyCollection<FtpEntryInfo>(safeItems);
	}

	private FtpEntryInfo CreateRootEntry()
	{
		var name = _profile.RootPath.IsRoot
			? _profile.DisplayName
			: _profile.RootPath.Name;

		return new FtpEntryInfo(_profile.RootPath, name, FtpEntryKind.Folder);
	}

	private void EnsureWithinRoot(FtpPath path)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (!path.IsWithin(_profile.RootPath, _profile.PathComparer))
		{
			throw new UnauthorizedAccessException($"FTP path '{path.Value}' is outside the configured root.");
		}
	}
}

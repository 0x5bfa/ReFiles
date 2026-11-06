// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

internal sealed class InMemoryFtpSessionFactory :
	IFtpSessionFactory
{
	private readonly Dictionary<string, FtpTestEntry> _entries;

	private readonly StringComparer _pathComparer;

	public IList<InMemoryFtpSession> Sessions { get; } = [];

	public FtpCredential? LastCredential { get; private set; }

	public Exception? CompleteTransferError { get; set; }

	public IReadOnlyCollection<string> Paths => _entries.Keys.ToArray();

	public InMemoryFtpSessionFactory(bool caseSensitive = true)
	{
		_pathComparer = caseSensitive
			? StringComparer.Ordinal
			: StringComparer.OrdinalIgnoreCase;
		_entries = new Dictionary<string, FtpTestEntry>(_pathComparer);
	}

	public void AddFolder(string path)
	{
		var ftpPath = FtpPath.Parse(path);
		_entries[ftpPath.Value] = new FtpTestEntry(new FtpEntryInfo(ftpPath, ftpPath.Name, FtpEntryKind.Folder), []);
	}

	public void AddFile(string path, byte[] content, DateTimeOffset? dateModified = null)
	{
		var ftpPath = FtpPath.Parse(path);
		_entries[ftpPath.Value] = new FtpTestEntry(new FtpEntryInfo(ftpPath, ftpPath.Name, FtpEntryKind.File, content.LongLength, dateModified), [.. content]);
	}

	public bool Contains(string path)
	{
		return _entries.ContainsKey(FtpPath.Parse(path).Value);
	}

	public byte[] ReadContent(string path)
	{
		return [.. _entries[FtpPath.Parse(path).Value]
			.Content];
	}

	public ValueTask<IFtpSession> ConnectAsync(FtpConnectionProfile profile, FtpCredential credential, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(credential);
		cancellationToken.ThrowIfCancellationRequested();

		LastCredential = credential;
		var session = new InMemoryFtpSession(_entries, _pathComparer)
		{
			CompleteTransferError = CompleteTransferError,
		};
		Sessions.Add(session);

		return ValueTask.FromResult<IFtpSession>(session);
	}
}

internal sealed class InMemoryFtpSession : IFtpSession
{
	private readonly Dictionary<string, FtpTestEntry> _entries;

	private readonly StringComparer _pathComparer;

	private FtpPath? _pendingWritePath;

	private MemoryStream? _pendingWriteStream;

	public bool IsDisposed { get; private set; }

	public int CompletedTransferCount { get; private set; }

	public Exception? CompleteTransferError { get; init; }

	public InMemoryFtpSession(Dictionary<string, FtpTestEntry> entries, StringComparer pathComparer)
	{
		this._entries = entries;
		this._pathComparer = pathComparer;
	}

	public ValueTask<FtpEntryInfo?> GetEntryAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		_entries.TryGetValue(path.Value, out var entry);

		return ValueTask.FromResult(entry?.Info);
	}

	public ValueTask<IReadOnlyList<FtpEntryInfo>> GetListingAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		var items = _entries.Values
			.Where(entry => entry.Info.Path.Parent is { } parent && _pathComparer.Equals(parent.Value, path.Value))
			.Select(static entry => entry.Info)
			.OrderBy(static entry => entry.Name, StringComparer.Ordinal)
			.ToArray();

		return ValueTask.FromResult<IReadOnlyList<FtpEntryInfo>>(items);
	}

	public ValueTask<Stream> OpenReadAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		if (!_entries.TryGetValue(path.Value, out var entry) || entry.Info.Kind is FtpEntryKind.Folder)
		{
			throw new FileNotFoundException("FTP test file was not found.", path.Value);
		}

		return ValueTask.FromResult<Stream>(new MemoryStream([.. entry.Content], writable: false));
	}

	public ValueTask<Stream> OpenWriteAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		_pendingWritePath = path;
		_pendingWriteStream = new MemoryStream();

		return ValueTask.FromResult<Stream>(_pendingWriteStream);
	}

	public ValueTask CompleteTransferAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		CompletedTransferCount++;
		if (CompleteTransferError is not null)
		{
			throw CompleteTransferError;
		}

		if (_pendingWritePath is { } path && _pendingWriteStream is { } stream)
		{
			var content = stream.ToArray();
			_entries[path.Value] = new FtpTestEntry(new FtpEntryInfo(path, path.Name, FtpEntryKind.File, content.LongLength), content);
			_pendingWritePath = null;
			_pendingWriteStream = null;
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask CreateFileAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		EnsureMissing(path);
		_entries[path.Value] = new FtpTestEntry(new FtpEntryInfo(path, path.Name, FtpEntryKind.File, 0), []);

		return ValueTask.CompletedTask;
	}

	public ValueTask CreateFolderAsync(FtpPath path, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		EnsureMissing(path);
		_entries[path.Value] = new FtpTestEntry(new FtpEntryInfo(path, path.Name, FtpEntryKind.Folder), []);

		return ValueTask.CompletedTask;
	}

	public ValueTask DeleteAsync(FtpPath path, FtpEntryKind kind, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		var keys = kind is FtpEntryKind.Folder
			? _entries.Keys
				.Where(candidate => _pathComparer.Equals(candidate, path.Value) || IsDescendant(candidate, path.Value))
				.ToArray()
			: [path.Value];
		foreach (var key in keys)
		{
			_entries.Remove(key);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask MoveAsync(FtpPath sourcePath, FtpPath destinationPath, FtpEntryKind kind, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();

		EnsureMissing(destinationPath);
		if (!_entries.TryGetValue(sourcePath.Value, out _))
		{
			throw new FileNotFoundException("FTP test item was not found.", sourcePath.Value);
		}

		var moving = _entries
			.Where(pair => _pathComparer.Equals(pair.Key, sourcePath.Value) || kind is FtpEntryKind.Folder && IsDescendant(pair.Key, sourcePath.Value))
			.OrderBy(pair => pair.Key.Length)
			.ToArray();
		foreach (var pair in moving)
		{
			_entries.Remove(pair.Key);
		}

		foreach (var pair in moving)
		{
			var suffix = pair.Key[sourcePath.Value.Length..];
			var newPath = FtpPath.Parse($"{destinationPath.Value}{suffix}");
			var oldInfo = pair.Value.Info;
			var newInfo = new FtpEntryInfo(newPath, suffix.Length is 0 ? destinationPath.Name : oldInfo.Name, oldInfo.Kind, oldInfo.Size, oldInfo.DateModified, oldInfo.DateCreated, oldInfo.LinkTarget);
			_entries[newPath.Value] = new FtpTestEntry(newInfo, pair.Value.Content);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;

		return ValueTask.CompletedTask;
	}

	private bool IsDescendant(string candidate, string parent)
	{
		return candidate.Length > parent.Length
			&& candidate[parent.Length] is '/'
			&& _pathComparer.Equals(candidate[..parent.Length], parent);
	}

	private void EnsureMissing(FtpPath path)
	{
		if (_entries.ContainsKey(path.Value))
		{
			throw new IOException($"FTP test item '{path.Value}' already exists.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);

	}
}

internal sealed record FtpTestEntry(FtpEntryInfo Info, byte[] Content);

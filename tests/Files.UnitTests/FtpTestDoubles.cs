// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Ftp;

namespace Files.UnitTests;

internal sealed class InMemoryFtpSessionFactory :
	IFtpSessionFactory
{
	private readonly Dictionary<string, FtpTestEntry> entries;
	private readonly StringComparer pathComparer;

	public InMemoryFtpSessionFactory(
		bool caseSensitive = true)
	{
		pathComparer = caseSensitive
			? StringComparer.Ordinal
			: StringComparer.OrdinalIgnoreCase;
		entries = new Dictionary<string, FtpTestEntry>(
			pathComparer);
	}

	public IList<InMemoryFtpSession> Sessions { get; } = [];

	public FtpCredential? LastCredential { get; private set; }

	public Exception? CompleteTransferError { get; set; }

	public IReadOnlyCollection<string> Paths => entries.Keys.ToArray();

	public void AddFolder(string path)
	{
		var ftpPath = FtpPath.Parse(path);
		entries[ftpPath.Value] = new FtpTestEntry(
			new FtpEntryInfo(
				ftpPath,
				ftpPath.Name,
				FtpEntryKind.Folder),
			[]);
	}

	public void AddFile(
		string path,
		byte[] content,
		DateTimeOffset? dateModified = null)
	{
		var ftpPath = FtpPath.Parse(path);
		entries[ftpPath.Value] = new FtpTestEntry(
			new FtpEntryInfo(
				ftpPath,
				ftpPath.Name,
				FtpEntryKind.File,
				content.LongLength,
				dateModified),
			content.ToArray());
	}

	public bool Contains(string path)
	{
		return entries.ContainsKey(FtpPath.Parse(path).Value);
	}

	public byte[] ReadContent(string path)
	{
		return entries[FtpPath.Parse(path).Value]
			.Content
			.ToArray();
	}

	public ValueTask<IFtpSession> ConnectAsync(
		FtpConnectionProfile profile,
		FtpCredential credential,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(credential);
		cancellationToken.ThrowIfCancellationRequested();

		LastCredential = credential;
		var session = new InMemoryFtpSession(
			entries,
			pathComparer)
		{
			CompleteTransferError = CompleteTransferError,
		};
		Sessions.Add(session);
		return ValueTask.FromResult<IFtpSession>(session);
	}
}

internal sealed class InMemoryFtpSession : IFtpSession
{
	private readonly Dictionary<string, FtpTestEntry> entries;
	private readonly StringComparer pathComparer;
	private FtpPath? pendingWritePath;
	private MemoryStream? pendingWriteStream;

	public InMemoryFtpSession(
		Dictionary<string, FtpTestEntry> entries,
		StringComparer pathComparer)
	{
		this.entries = entries;
		this.pathComparer = pathComparer;
	}

	public bool IsDisposed { get; private set; }

	public int CompletedTransferCount { get; private set; }

	public Exception? CompleteTransferError { get; init; }

	public ValueTask<FtpEntryInfo?> GetEntryAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		entries.TryGetValue(path.Value, out var entry);
		return ValueTask.FromResult(entry?.Info);
	}

	public ValueTask<IReadOnlyList<FtpEntryInfo>> GetListingAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		var items = entries.Values
			.Where(entry => entry.Info.Path.Parent is { } parent
				&& pathComparer.Equals(
					parent.Value,
					path.Value))
			.Select(static entry => entry.Info)
			.OrderBy(static entry => entry.Name, StringComparer.Ordinal)
			.ToArray();
		return ValueTask.FromResult<IReadOnlyList<FtpEntryInfo>>(
			items);
	}

	public ValueTask<Stream> OpenReadAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		if (!entries.TryGetValue(path.Value, out var entry)
			|| entry.Info.Kind is FtpEntryKind.Folder)
		{
			throw new FileNotFoundException(
				"FTP test file was not found.",
				path.Value);
		}

		return ValueTask.FromResult<Stream>(
			new MemoryStream(
				entry.Content.ToArray(),
				writable: false));
	}

	public ValueTask<Stream> OpenWriteAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		pendingWritePath = path;
		pendingWriteStream = new MemoryStream();
		return ValueTask.FromResult<Stream>(pendingWriteStream);
	}

	public ValueTask CompleteTransferAsync(
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		CompletedTransferCount++;
		if (CompleteTransferError is not null)
		{
			throw CompleteTransferError;
		}

		if (pendingWritePath is { } path
			&& pendingWriteStream is { } stream)
		{
			var content = stream.ToArray();
			entries[path.Value] = new FtpTestEntry(
				new FtpEntryInfo(
					path,
					path.Name,
					FtpEntryKind.File,
					content.LongLength),
				content);
			pendingWritePath = null;
			pendingWriteStream = null;
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask CreateFileAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		EnsureMissing(path);
		entries[path.Value] = new FtpTestEntry(
			new FtpEntryInfo(
				path,
				path.Name,
				FtpEntryKind.File,
				0),
			[]);
		return ValueTask.CompletedTask;
	}

	public ValueTask CreateFolderAsync(
		FtpPath path,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		EnsureMissing(path);
		entries[path.Value] = new FtpTestEntry(
			new FtpEntryInfo(
				path,
				path.Name,
				FtpEntryKind.Folder),
			[]);
		return ValueTask.CompletedTask;
	}

	public ValueTask DeleteAsync(
		FtpPath path,
		FtpEntryKind kind,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		var keys = kind is FtpEntryKind.Folder
			? entries.Keys
				.Where(candidate =>
					pathComparer.Equals(candidate, path.Value)
					|| IsDescendant(candidate, path.Value))
				.ToArray()
			: [path.Value];
		foreach (var key in keys)
		{
			entries.Remove(key);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask MoveAsync(
		FtpPath sourcePath,
		FtpPath destinationPath,
		FtpEntryKind kind,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		EnsureMissing(destinationPath);
		if (!entries.TryGetValue(sourcePath.Value, out _))
		{
			throw new FileNotFoundException(
				"FTP test item was not found.",
				sourcePath.Value);
		}

		var moving = entries
			.Where(pair =>
				pathComparer.Equals(
					pair.Key,
					sourcePath.Value)
				|| kind is FtpEntryKind.Folder
					&& IsDescendant(
						pair.Key,
						sourcePath.Value))
			.OrderBy(pair => pair.Key.Length)
			.ToArray();
		foreach (var pair in moving)
		{
			entries.Remove(pair.Key);
		}

		foreach (var pair in moving)
		{
			var suffix = pair.Key[sourcePath.Value.Length..];
			var newPath = FtpPath.Parse(
				$"{destinationPath.Value}{suffix}");
			var oldInfo = pair.Value.Info;
			var newInfo = new FtpEntryInfo(
				newPath,
				suffix.Length is 0
					? destinationPath.Name
					: oldInfo.Name,
				oldInfo.Kind,
				oldInfo.Size,
				oldInfo.DateModified,
				oldInfo.DateCreated,
				oldInfo.LinkTarget);
			entries[newPath.Value] = new FtpTestEntry(
				newInfo,
				pair.Value.Content);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		return ValueTask.CompletedTask;
	}

	private bool IsDescendant(
		string candidate,
		string parent)
	{
		return candidate.Length > parent.Length
			&& candidate[parent.Length] is '/'
			&& pathComparer.Equals(
				candidate[..parent.Length],
				parent);
	}

	private void EnsureMissing(FtpPath path)
	{
		if (entries.ContainsKey(path.Value))
		{
			throw new IOException(
				$"FTP test item '{path.Value}' already exists.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);
	}
}

internal sealed record FtpTestEntry(
	FtpEntryInfo Info,
	byte[] Content);

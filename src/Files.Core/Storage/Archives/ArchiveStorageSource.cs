// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Archives;

internal sealed class ArchiveStorageSource : IStorageSource
{
	private readonly Stream _input;
	private readonly IArchiveBackend _backend;
	private readonly IArchiveCredentialResolver? _credentials;
	private readonly SemaphoreSlim _readerLock = new(1, 1);
	private readonly Lock _disposalLock = new();
	private IArchiveReadSession? _session;
	private int _credentialAttempt;
	private Task? _disposeTask;
	private volatile bool _isDisposed;

	public IStorageSource RootSource { get; }
	public ArchiveLocator Locator { get; }
	public StorableReference Archive { get; }
	public ArchiveCatalog Catalog { get; }
	public StorageSourceId SourceId { get; } = new($"archive-session-{Guid.NewGuid():N}");
	public string SourceType => "archive";
	public string DisplayName { get; }

	public ArchiveStorageSource(IStorageSource rootSource, ArchiveLocator locator, ArchiveOpenRequest request, IArchiveReadSession session, IArchiveBackend backend,
		IArchiveCredentialResolver? credentials)
	{
		RootSource = rootSource;
		Locator = locator;
		Archive = request.Archive;
		DisplayName = request.Name;
		_input = request.Input;
		_session = session;
		_backend = backend;
		_credentials = credentials;
		_credentialAttempt = request.CredentialAttempt;
		Catalog = new ArchiveCatalog(session.Entries, request.Name);
	}

	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		cancellationToken.ThrowIfCancellationRequested();

		await Task.CompletedTask.ConfigureAwait(false);
		yield return (IFolder)Create(Catalog.Root);
	}

	public bool CanResolve(StorageAddress address)
	{
		return address.Scheme == "archive-entry" && address.Value.StartsWith(SourceId.Value + "/", StringComparison.Ordinal);
	}

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		cancellationToken.ThrowIfCancellationRequested();

		if (!CanResolve(address))
		{
			throw new ArgumentException("The address belongs to another archive session.", nameof(address));
		}

		return ValueTask.FromResult(Create(Catalog.GetNode(Uri.UnescapeDataString(address.Value[(SourceId.Value.Length + 1)..]))));
	}

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		cancellationToken.ThrowIfCancellationRequested();

		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException("The reference belongs to another archive session.", nameof(reference));
		}

		return ValueTask.FromResult(Create(Catalog.GetNode(reference.ItemId)));
	}

	public IFolder GetFolder(string path)
	{
		ThrowIfDisposed();

		return (IFolder)Create(Catalog.GetFolder(path));
	}

	public IStorable Create(ArchiveNode node)
	{
		ThrowIfDisposed();

		return node.IsDirectory ? new ArchiveFolder(this, node) : new ArchiveFile(this, node);
	}

	public async Task<Stream> OpenReadAsync(string entryId, CancellationToken cancellationToken)
	{
		await _readerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();

			while (true)
			{
				try
				{
					var stream = await (_session ?? throw new ArchiveOpenException("The archive reader is unavailable.")).OpenReadAsync(entryId, cancellationToken).ConfigureAwait(false);
					try
					{
						cancellationToken.ThrowIfCancellationRequested();

						if (!stream.CanRead || !stream.CanSeek)
						{
							throw new ArchiveOpenException("The archive reader returned content that is not readable and seekable.");
						}

						stream.Position = 0;

						return stream;
					}
					catch
					{
						await stream.DisposeAsync().ConfigureAwait(false);
						throw;
					}
				}
				catch (ArchiveCredentialRequiredException)
				{
					await ReopenAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}
		finally
		{
			_readerLock.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_isDisposed = true;
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async Task ReopenAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			var challenge = new ArchiveCredentialChallenge(Archive, DisplayName, ++_credentialAttempt, previousCredentialRejected: true);
			if (_credentials is null || _credentialAttempt > 5)
			{
				throw new ArchiveCredentialRequiredException(challenge);
			}

			var credential = await _credentials.ResolveAsync(challenge, cancellationToken).ConfigureAwait(false)
				?? throw new OperationCanceledException("Archive credential entry was canceled.", cancellationToken);
			var previous = _session;
			_session = null;
			if (previous is not null)
			{
				await previous.DisposeAsync().ConfigureAwait(false);
			}

			_input.Position = 0;
			var result = await _backend.TryOpenAsync(new ArchiveOpenRequest(Archive, DisplayName, _input, credential, _credentialAttempt), cancellationToken).ConfigureAwait(false);
			if (result is ArchiveOpenResult.CredentialRequired)
			{
				continue;
			}

			if (result is not ArchiveOpenResult.Success success)
			{
				throw new ArchiveOpenException("The selected archive reader could not reopen the input.", (result as ArchiveOpenResult.Failed)?.Error);
			}

			if (!Catalog.Entries.SequenceEqual(success.Session.Entries))
			{
				await success.Session.DisposeAsync().ConfigureAwait(false);
				throw new ArchiveOpenException("The archive catalog changed while reopening its reader.");
			}

			_session = success.Session;

			return;
		}
	}

	private async Task DisposeCoreAsync()
	{
		await _readerLock.WaitAsync().ConfigureAwait(false);
		try
		{
			try
			{
				if (_session is not null)
				{
					await _session.DisposeAsync().ConfigureAwait(false);
				}
			}
			finally
			{
				await _input.DisposeAsync().ConfigureAwait(false);
			}
		}
		finally
		{
			_readerLock.Release();
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}

	private abstract class ArchiveItem(ArchiveStorageSource source, ArchiveNode node) : IStorableChild, IArchiveEntry, IStorageAddressSource
	{
		protected ArchiveStorageSource Source { get; } = source;
		protected ArchiveNode Node { get; } = node;
		public string Id => Node.Id;
		public string Name => Node.Name;
		public ArchiveLocator Locator => Source.Locator;
		public StorableReference Archive => Source.Archive;
		public string EntryId => Node.EntryId ?? Node.Id;
		public string EntryPath => Node.Path;
		public StorageAddress Address => new("archive-entry", Source.SourceId.Value + "/" + Uri.EscapeDataString(Id));

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
		{
			Source.ThrowIfDisposed();

			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult(Node.Path.Length == 0 ? null : (IFolder?)Source.GetFolder(ArchiveEntryPath.GetParent(Node.Path)));
		}
	}

	private sealed class ArchiveFolder(ArchiveStorageSource source, ArchiveNode node) : ArchiveItem(source, node), IFolder
	{
		public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Source.ThrowIfDisposed();

			await Task.CompletedTask.ConfigureAwait(false);
			foreach (var child in Source.Catalog.GetChildren(Node.Path))
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (child.IsDirectory ? type.HasFlag(StorableType.Folder) : type.HasFlag(StorableType.File))
				{
					yield return (IStorableChild)Source.Create(child);
				}
			}
		}
	}

	private sealed class ArchiveFile(ArchiveStorageSource source, ArchiveNode node) : ArchiveItem(source, node), IFile, IReadableContent, IArchiveInput
	{
		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			return Source.OpenReadAsync(EntryId, cancellationToken);
		}
	}
}

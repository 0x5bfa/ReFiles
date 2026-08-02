// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using global::SevenZip;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed class SevenZipArchiveMount
	: IArchiveMount, IStorageSource
{
	public const string EntryAddressScheme = "archive-entry";

	private const int MaximumCredentialAttempts = 5;

	private readonly Stream _archiveStream;

	private readonly SevenZipArchiveIndex _index;

	private readonly SemaphoreSlim _extractorLock = new(1, 1);

	private readonly Lock _disposalLock = new();

	private readonly IArchiveCredentialResolver? _credentialResolver;

	private SevenZipExtractor _extractor;

	private int _credentialAttempt;

	private Task? _disposeTask;

	private volatile bool _isDisposed;

	public string BackendId =>
		SevenZipArchiveBackend.DefaultBackendId;

	public StorableReference Archive { get; }

	public IStorageSource ItemSource => this;

	public IFolder Root { get; }

	public StorageSourceId SourceId { get; }

	public string SourceType =>
		SevenZipArchiveBackend.DefaultBackendId;

	public string DisplayName { get; }

	public SevenZipArchiveMount(ArchiveMountRequest request, Stream archiveStream, SevenZipExtractor extractor, SevenZipArchiveIndex index)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(archiveStream);
		ArgumentNullException.ThrowIfNull(extractor);
		ArgumentNullException.ThrowIfNull(index);

		Archive = request.Archive;
		DisplayName = request.ArchiveModel.Name;
		_archiveStream = archiveStream;
		_extractor = extractor;
		_index = index;
		_credentialAttempt = request.CredentialAttempt;
		_credentialResolver = request.CredentialResolver;
		SourceId = CreateSourceId(request.Archive);
		Root = CreateFolder(string.Empty);
	}

	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();

		await Task.CompletedTask.ConfigureAwait(false);
		yield return Root;
	}

	public bool CanResolve(StorageAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);

		return address.Scheme.Equals(EntryAddressScheme, StringComparison.OrdinalIgnoreCase);
	}

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(address);

		if (!CanResolve(address))
		{
			throw new ArgumentException($"Address scheme '{address.Scheme}' is not supported.", nameof(address));
		}

		return ResolveAsync(address.Value is "/" ? string.Empty : address.Value, cancellationToken);
	}

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(reference);

		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException($"Reference belongs to storage source '{reference.SourceId}'.", nameof(reference));
		}

		return ResolveAsync(reference.ItemId is "/" ? string.Empty : reference.ItemId, cancellationToken);
	}

	public ValueTask<IStorable> ResolveAsync(string entryPath, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();

		var node = _index.GetNode(entryPath);

		return ValueTask.FromResult<IStorable>(node.IsDirectory ? CreateFolder(node.Path) : CreateFile(node.Path));
	}

	internal IReadOnlyList<SevenZipArchiveNode> GetChildren(string entryPath)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _index.GetChildren(entryPath);
	}

	internal SevenZipArchiveFolder CreateFolder(string entryPath)
	{
		var node = _index.GetNode(entryPath);

		return new SevenZipArchiveFolder(this, node);
	}

	internal SevenZipArchiveFile CreateFile(string entryPath)
	{
		var node = _index.GetNode(entryPath);

		return new SevenZipArchiveFile(this, node);
	}

	internal async Task<Stream> OpenFileAsync(SevenZipArchiveNode node, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (node.EntryIndex is not { } entryIndex)
		{
			throw new InvalidOperationException($"Archive entry '{node.Path}' has no extraction index.");
		}

		var output = ArchiveStreamResolver.CreateTemporaryStream();
		try
		{
			await _extractorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				while (true)
				{
					ObjectDisposedException.ThrowIf(_isDisposed, this);
					cancellationToken.ThrowIfCancellationRequested();

					try
					{
						await _extractor.ExtractFileAsync(entryIndex, output).ConfigureAwait(false);
						cancellationToken.ThrowIfCancellationRequested();

						break;
					}
					catch (Exception error)
						when (SevenZipArchiveBackend.IsPasswordFailure(error))
					{
						await ReplaceCredentialAsync(output, cancellationToken).ConfigureAwait(false);
					}
				}
			}
			finally
			{
				_extractorLock.Release();
			}

			output.Position = 0;

			return output;
		}
		catch
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (_disposalLock)
		{
			_disposeTask ??= DisposeCoreAsync();

			return new ValueTask(_disposeTask);
		}
	}

	private async ValueTask ReplaceCredentialAsync(Stream output, CancellationToken cancellationToken)
	{
		output.Position = 0;
		output.SetLength(0);

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var challenge = new ArchiveCredentialChallenge(Archive, DisplayName, _credentialAttempt + 1, previousCredentialRejected: true);
			if (_credentialResolver is null || challenge.Attempt > MaximumCredentialAttempts)
			{
				throw new ArchiveCredentialRequiredException(challenge);
			}

			var credential = await _credentialResolver.ResolveAsync(challenge, cancellationToken).ConfigureAwait(false);
			if (credential is null)
			{
				throw new OperationCanceledException("The archive credential request was canceled.");
			}

			SevenZipExtractor? replacement = null;
			try
			{
				_archiveStream.Position = 0;
				replacement =
					SevenZipArchiveBackend.CreateExtractor(_archiveStream, credential);
				_ = replacement.ArchiveFileData;
				var previous = _extractor;
				_extractor = replacement;
				replacement = null;
				previous.Dispose();
				_credentialAttempt = challenge.Attempt;

				return;
			}
			catch (Exception error)
				when (SevenZipArchiveBackend.IsPasswordFailure(error))
			{
				_credentialAttempt = challenge.Attempt;
			}
			finally
			{
				replacement?.Dispose();
			}
		}
	}

	private async Task DisposeCoreAsync()
	{
		_isDisposed = true;
		await _extractorLock.WaitAsync().ConfigureAwait(false);
		List<Exception>? errors = null;
		try
		{
			try
			{
				_extractor.Dispose();
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}

			try
			{
				await _archiveStream.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}
		}
		finally
		{
			_extractorLock.Release();
		}

		GC.SuppressFinalize(this);
		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException("The archive extractor and backing stream could not be disposed.", errors);
		}
	}

	private static StorageSourceId CreateSourceId(StorableReference archive)
	{
		var identity = Encoding.UTF8.GetBytes($"{archive.SourceId.Value}\0{archive.ItemId}");
		var hash = SHA256.HashData(identity);

		return new StorageSourceId($"archive-{Convert.ToHexString(hash)}");
	}
}

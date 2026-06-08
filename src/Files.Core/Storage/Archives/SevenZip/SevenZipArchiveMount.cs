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
	private readonly Stream archiveStream;
	private readonly SevenZipArchiveIndex index;
	private readonly SemaphoreSlim extractorLock = new(1, 1);
	private readonly object disposalLock = new();
	private readonly IArchiveCredentialResolver? credentialResolver;
	private SevenZipExtractor extractor;
	private int credentialAttempt;
	private Task? disposeTask;
	private volatile bool isDisposed;

	public SevenZipArchiveMount(
		ArchiveMountRequest request,
		Stream archiveStream,
		SevenZipExtractor extractor,
		SevenZipArchiveIndex index)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(archiveStream);
		ArgumentNullException.ThrowIfNull(extractor);
		ArgumentNullException.ThrowIfNull(index);

		Archive = request.Archive;
		DisplayName = request.ArchiveModel.Name;
		this.archiveStream = archiveStream;
		this.extractor = extractor;
		this.index = index;
		credentialAttempt = request.CredentialAttempt;
		credentialResolver = request.CredentialResolver;
		SourceId = CreateSourceId(request.Archive);
		Root = CreateFolder(string.Empty);
	}

	public string BackendId =>
		SevenZipArchiveBackend.DefaultBackendId;

	public StorableReference Archive { get; }

	public IStorageSource ItemSource => this;

	public IFolder Root { get; }

	public StorageSourceId SourceId { get; }

	public string SourceType =>
		SevenZipArchiveBackend.DefaultBackendId;

	public string DisplayName { get; }

	public async IAsyncEnumerable<IFolder> GetRootsAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		await Task.CompletedTask.ConfigureAwait(false);
		yield return Root;
	}

	public bool CanResolve(StorageAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		return address.Scheme.Equals(
			EntryAddressScheme,
			StringComparison.OrdinalIgnoreCase);
	}

	public ValueTask<IStorable> ResolveAsync(
		StorageAddress address,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		ArgumentNullException.ThrowIfNull(address);
		if (!CanResolve(address))
		{
			throw new ArgumentException(
				$"Address scheme '{address.Scheme}' is not supported.",
				nameof(address));
		}

		return ResolveAsync(
			address.Value is "/" ? string.Empty : address.Value,
			cancellationToken);
	}

	public ValueTask<IStorable> ResolveAsync(
		StorableReference reference,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		ArgumentNullException.ThrowIfNull(reference);
		if (reference.SourceId != SourceId)
		{
			throw new ArgumentException(
				$"Reference belongs to storage source '{reference.SourceId}'.",
				nameof(reference));
		}

		return ResolveAsync(
			reference.ItemId is "/"
				? string.Empty
				: reference.ItemId,
			cancellationToken);
	}

	public ValueTask<IStorable> ResolveAsync(
		string entryPath,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();

		var node = index.GetNode(entryPath);
		return ValueTask.FromResult<IStorable>(
			node.IsDirectory
				? CreateFolder(node.Path)
				: CreateFile(node.Path));
	}

	internal IReadOnlyList<SevenZipArchiveNode> GetChildren(
		string entryPath)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		return index.GetChildren(entryPath);
	}

	internal SevenZipArchiveFolder CreateFolder(string entryPath)
	{
		var node = index.GetNode(entryPath);
		return new SevenZipArchiveFolder(this, node);
	}

	internal SevenZipArchiveFile CreateFile(string entryPath)
	{
		var node = index.GetNode(entryPath);
		return new SevenZipArchiveFile(this, node);
	}

	internal async Task<Stream> OpenFileAsync(
		SevenZipArchiveNode node,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
		if (node.EntryIndex is not { } entryIndex)
		{
			throw new InvalidOperationException(
				$"Archive entry '{node.Path}' has no extraction index.");
		}

		var output = ArchiveStreamResolver.CreateTemporaryStream();
		try
		{
			await extractorLock
				.WaitAsync(cancellationToken)
				.ConfigureAwait(false);
			try
			{
				while (true)
				{
					ObjectDisposedException.ThrowIf(isDisposed, this);
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						await extractor
							.ExtractFileAsync(entryIndex, output)
							.ConfigureAwait(false);
						cancellationToken.ThrowIfCancellationRequested();
						break;
					}
					catch (Exception error)
						when (SevenZipArchiveBackend.IsPasswordFailure(error))
					{
						await ReplaceCredentialAsync(
							output,
							cancellationToken).ConfigureAwait(false);
					}
				}
			}
			finally
			{
				extractorLock.Release();
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

	private async ValueTask ReplaceCredentialAsync(
		Stream output,
		CancellationToken cancellationToken)
	{
		output.Position = 0;
		output.SetLength(0);

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var challenge = new ArchiveCredentialChallenge(
				Archive,
				DisplayName,
				credentialAttempt + 1,
				previousCredentialRejected: true);
			if (credentialResolver is null
				|| challenge.Attempt
					> MaximumCredentialAttempts)
			{
				throw new ArchiveCredentialRequiredException(
					challenge);
			}

			var credential = await credentialResolver
				.ResolveAsync(
					challenge,
					cancellationToken)
				.ConfigureAwait(false);
			if (credential is null)
			{
				throw new OperationCanceledException(
					"The archive credential request was canceled.");
			}

			SevenZipExtractor? replacement = null;
			try
			{
				archiveStream.Position = 0;
				replacement =
					SevenZipArchiveBackend.CreateExtractor(
						archiveStream,
						credential);
				_ = replacement.ArchiveFileData;
				var previous = extractor;
				extractor = replacement;
				replacement = null;
				previous.Dispose();
				credentialAttempt = challenge.Attempt;
				return;
			}
			catch (Exception error)
				when (SevenZipArchiveBackend.IsPasswordFailure(
					error))
			{
				credentialAttempt = challenge.Attempt;
			}
			finally
			{
				replacement?.Dispose();
			}
		}
	}

	public ValueTask DisposeAsync()
	{
		lock (disposalLock)
		{
			disposeTask ??= DisposeCoreAsync();
			return new ValueTask(disposeTask);
		}
	}

	private async Task DisposeCoreAsync()
	{
		isDisposed = true;
		await extractorLock.WaitAsync().ConfigureAwait(false);
		List<Exception>? errors = null;
		try
		{
			try
			{
				extractor.Dispose();
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}

			try
			{
				await archiveStream
					.DisposeAsync()
					.ConfigureAwait(false);
			}
			catch (Exception error)
			{
				(errors ??= []).Add(error);
			}
		}
		finally
		{
			extractorLock.Release();
		}

		GC.SuppressFinalize(this);
		if (errors is { Count: 1 })
		{
			throw errors[0];
		}

		if (errors is { Count: > 1 })
		{
			throw new AggregateException(
				"The archive extractor and backing stream could not be disposed.",
				errors);
		}
	}

	private static StorageSourceId CreateSourceId(
		StorableReference archive)
	{
		var identity = Encoding.UTF8.GetBytes(
			$"{archive.SourceId.Value}\0{archive.ItemId}");
		var hash = SHA256.HashData(identity);
		return new StorageSourceId(
			$"archive-{Convert.ToHexString(hash)}");
	}
}

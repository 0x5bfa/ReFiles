// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.UnitTests;

internal sealed class ArchiveTestSource(string name = "example.zip", byte[]? content = null, bool readable = true) : IStorageSource
{
	public StorageSourceId SourceId { get; } = new("archive-test");
	public string SourceType => "test";
	public string DisplayName => "Archive test";
	public string Name { get; } = name;
	public byte[] Content { get; } = content ?? [1];
	public StorableReference Reference => new(SourceId, "container");
	public bool IsDisposed { get; private set; }

	public bool CanResolve(StorageAddress address)
	{
		return false;
	}

	public async IAsyncEnumerable<IFolder> GetRootsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await Task.CompletedTask.ConfigureAwait(false);
		yield break;
	}

	public ValueTask<IStorable> ResolveAsync(StorageAddress address, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ValueTask<IStorable> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		cancellationToken.ThrowIfCancellationRequested();

		if (reference.SourceId != SourceId || reference.ItemId != "container")
		{
			throw new FileNotFoundException();
		}

		return ValueTask.FromResult<IStorable>(readable ? new ArchiveTestFile(Name, Content) : new ArchiveTestItem(Name));
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;

		return ValueTask.CompletedTask;
	}
}

internal class ArchiveTestItem(string name) : IFile
{
	public string Id => "container";
	public string Name => name;
}

internal sealed class ArchiveTestFile(string name, byte[] content) : ArchiveTestItem(name), IReadableContent
{
	public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
	}
}

internal sealed class ArchiveTestBackend : IArchiveBackend
{
	private readonly Func<ArchiveOpenRequest, ArchiveOpenResult> _open;

	public string Id { get; }
	public int Priority { get; }
	public int OpenCount { get; private set; }

	public ArchiveTestBackend(Func<ArchiveOpenRequest, ArchiveOpenResult> open, string id = "test", int priority = 100)
	{
		_open = open;
		Id = id;
		Priority = priority;
	}

	public ValueTask<ArchiveOpenResult> TryOpenAsync(ArchiveOpenRequest request, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		OpenCount++;

		return ValueTask.FromResult(_open(request));
	}

	public StorableModelFactory CreateModels(IArchiveCredentialResolver? credentials = null)
	{
		var readers = new ArchiveBackendSelector([this]);
		var capabilities = new CapabilityBuilder().Add<IArchiveSource>(new ArchiveSourceFactory(readers: readers, credentials: credentials)).Build();

		return new StorableModelFactory(capabilities);
	}
}

internal sealed class ArchiveTestSession(IReadOnlyList<ArchiveEntryInfo> entries, Func<string, CancellationToken, Task<Stream>>? read = null, string backendId = "test") : IArchiveReadSession
{
	public string BackendId => backendId;
	public IReadOnlyList<ArchiveEntryInfo> Entries { get; } = entries;
	public int DisposeCount { get; private set; }

	public Task<Stream> OpenReadAsync(string entryId, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(DisposeCount != 0, this);

		cancellationToken.ThrowIfCancellationRequested();

		return read?.Invoke(entryId, cancellationToken) ?? Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(entryId), writable: false));
	}

	public ValueTask DisposeAsync()
	{
		DisposeCount++;

		return ValueTask.CompletedTask;
	}
}

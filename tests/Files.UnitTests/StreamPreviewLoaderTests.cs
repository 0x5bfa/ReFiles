// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Previews;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.UnitTests;

[TestClass]
public sealed class StreamPreviewLoaderTests
{
	[TestMethod]
	public void ResolverUsesCaseInsensitiveExtensionsAndRejectsUnsupportedItems()
	{
		var resolver = new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".TXT", "text/plain"), ]);
		var source = new TestStorageSource();
		var file = new TestFile("file", "readme.TxT", _ => Task.FromResult<Stream>(new MemoryStream()));
		var context = CreateContext(source, file);

		Assert.IsTrue(resolver.TryResolve(context, out var contentType));
		Assert.AreEqual("text/plain", contentType.MediaType);

		var unsupported = new TestStorable("other", "readme.bin");
		Assert.IsFalse(resolver.TryResolve(CreateContext(source, unsupported), out _));

		var addressedFile = new TestFile("addressed", "Readme", _ => Task.FromResult<Stream>(new MemoryStream()), @"C:\Content\readme.txt");
		Assert.IsTrue(resolver.TryResolve(CreateContext(source, addressedFile), out contentType));
		Assert.AreEqual("text/plain", contentType.MediaType);
	}

	[TestMethod]
	public void ResolverRejectsInvalidAndDuplicateExtensions()
	{
		Assert.Throws<ArgumentException>(() => new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>("txt", "text/plain"), ]));

		Assert.Throws<ArgumentException>(() => new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".txt", "text/plain"), new KeyValuePair<string, string>(".TXT", "text/csv"), ]));

		Assert.Throws<ArgumentException>(() => new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".txt", ""), ]));
}

	[TestMethod]
	public async Task LoaderAcceptsKnownFilesAndDoesNotOpenUnknownExtensions()
	{
		var source = new TestStorageSource();
		var loader = CreateLoader();
		var known = new TestFile("known", "file.txt", _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
		var unknown = new TestFile("unknown", "file.bin", _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));

		Assert.IsTrue(loader.CanLoad(CreateContext(source, known)));
		Assert.IsFalse(loader.CanLoad(CreateContext(source, unknown)));

		var result = await loader.GetPreviewAsync(new PreviewRequest(), CreateContext(source, unknown));

		Assert.IsNull(result);
		Assert.AreEqual(0, unknown.OpenCount);
}

	[TestMethod]
	public async Task PolicyBlocksBeforeOpeningTheFileAndPreservesHydrationPolicy()
	{
		var source = new TestStorageSource();
		var stream = new TrackingMemoryStream(Encoding.UTF8.GetBytes("content"));
		var file = new TestFile("file", "readme.txt", _ => Task.FromResult<Stream>(stream));
		var policy = new TestPolicy { BlockReason = PreviewBlockReason.RequiresHydration };
		var loader = CreateLoader(policy);

		var result = await loader.GetPreviewAsync(new PreviewRequest(hydrationPolicy: PreviewHydrationPolicy.AllowHydration), CreateContext(source, file));

		Assert.IsInstanceOfType<BlockedPreviewResult>(result);
		Assert.AreEqual(PreviewBlockReason.RequiresHydration, ((BlockedPreviewResult)result!).Reason);
		Assert.AreEqual(PreviewHydrationPolicy.AllowHydration, policy.Request!.HydrationPolicy);
		Assert.AreEqual(0, file.OpenCount);
		Assert.AreEqual(0, stream.DisposeCount);
}

	[TestMethod]
	public async Task SeekableStreamsUseLengthAndTransferOwnershipOnlyOnSuccess()
	{
		var source = new TestStorageSource();
		var withinLimit = new TrackingMemoryStream(Encoding.UTF8.GetBytes("abc"));
		var withinFile = new TestFile("within", "file.txt", _ => Task.FromResult<Stream>(withinLimit));
		var loader = CreateLoader();

		var withinResult = await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: 3), CreateContext(source, withinFile));

		var streamResult = (StreamPreviewResult)withinResult!;
		Assert.AreSame(withinLimit, streamResult.Content);
		Assert.AreEqual(3, streamResult.ContentLength);
		Assert.AreEqual(0, withinLimit.DisposeCount);
		await streamResult.DisposeAsync();
		await streamResult.DisposeAsync();
		Assert.AreEqual(1, withinLimit.DisposeCount);

		var overLimit = new TrackingMemoryStream(Encoding.UTF8.GetBytes("abcd"));
		var overFile = new TestFile("over", "file.txt", _ => Task.FromResult<Stream>(overLimit));
		var blocked = await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: 3), CreateContext(source, overFile));

		Assert.IsInstanceOfType<BlockedPreviewResult>(blocked);
		Assert.AreEqual(1, overLimit.DisposeCount);
}

	[TestMethod]
	public async Task NonSeekableStreamsAreBufferedOnlyWhenAByteLimitIsRequired()
	{
		var source = new TestStorageSource();
		var loader = CreateLoader();
		var content = Encoding.UTF8.GetBytes("hello");
		var bufferedSource = new NonSeekableStream(content);
		var bufferedFile = new TestFile("buffered", "file.txt", _ => Task.FromResult<Stream>(bufferedSource));

		var buffered = (StreamPreviewResult)(await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: content.Length), CreateContext(source, bufferedFile)))!;

		Assert.AreNotSame(bufferedSource, buffered.Content);
		Assert.AreEqual(content.Length, buffered.ContentLength);
		Assert.AreEqual(0, buffered.Content.Position);
		Assert.AreEqual(1, bufferedSource.DisposeCount);
		Assert.AreEqual("hello", await ReadTextAsync(buffered.Content));
		await buffered.DisposeAsync();

		var unboundedSource = new NonSeekableStream(content);
		var unboundedFile = new TestFile("unbounded", "file.txt", _ => Task.FromResult<Stream>(unboundedSource));
		var unbounded = (StreamPreviewResult)(await loader.GetPreviewAsync(new PreviewRequest(), CreateContext(source, unboundedFile)))!;

		Assert.AreSame(unboundedSource, unbounded.Content);
		Assert.IsNull(unbounded.ContentLength);
		Assert.AreEqual(0, unboundedSource.DisposeCount);
		await unbounded.DisposeAsync();
		Assert.AreEqual(1, unboundedSource.DisposeCount);

		var oversizedSource = new NonSeekableStream(Encoding.UTF8.GetBytes("hello!"));
		var oversizedFile = new TestFile("oversized", "file.txt", _ => Task.FromResult<Stream>(oversizedSource));
		var oversized = await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: content.Length), CreateContext(source, oversizedFile));

		Assert.IsInstanceOfType<BlockedPreviewResult>(oversized);
		Assert.AreEqual(1, oversizedSource.DisposeCount);
}

	[TestMethod]
	public async Task ReadFailureAndCancellationDisposeTheOpenedStream()
	{
		var source = new TestStorageSource();
		var loader = CreateLoader();
		var failedStream = new NonSeekableStream([1, 2, 3])
		{
			ReadException = new IOException("read failed"),
		};
		var failedFile = new TestFile("failed", "file.txt", _ => Task.FromResult<Stream>(failedStream));

		await Assert.ThrowsAsync<IOException>(async () => await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: 10), CreateContext(source, failedFile)));
		Assert.AreEqual(1, failedStream.DisposeCount);

		using var cancellation = new CancellationTokenSource();
		var cancelledStream = new NonSeekableStream([1, 2, 3])
		{
			BeforeRead = cancellation.Cancel,
		};
		var cancelledFile = new TestFile("cancelled", "file.txt", _ => Task.FromResult<Stream>(cancelledStream));

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await loader.GetPreviewAsync(new PreviewRequest(maximumBytes: 10), CreateContext(source, cancelledFile), cancellation.Token));
		Assert.AreEqual(1, cancelledStream.DisposeCount);

		using var cancelledAfterOpen = new CancellationTokenSource();
		var openedThenCancelledStream = new NonSeekableStream([1, 2, 3]);
		var openedThenCancelledFile = new TestFile("opened-cancelled", "file.txt", _ => { cancelledAfterOpen.Cancel(); return Task.FromResult<Stream>(openedThenCancelledStream); });

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await loader.GetPreviewAsync(new PreviewRequest(), CreateContext(source, openedThenCancelledFile), cancelledAfterOpen.Token));
		Assert.AreEqual(1, openedThenCancelledStream.DisposeCount);
}

	[TestMethod]
	public async Task RegistryAndBrowsePreviewHydrateAFileThroughTheLoader()
	{
		var source = new TestStorageSource();
		var content = Encoding.UTF8.GetBytes("preview content");
		var openedStream = new TrackingMemoryStream(content);
		var file = new TestFile("file", "note.txt", _ => Task.FromResult<Stream>(openedStream));
		var fileReference = new StorableReference(source.SourceId, file.Id, new StorageAddress("test", file.Name));
		var policy = new TestPolicy();
		var loader = CreateLoader(policy);
		var featureRegistry = new ItemFeatureBuilder()
			.Add<IPreviewSource>(new PreviewSourceFactory(loader), priority: 100)
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner())
			.Build();
		var fileModel = new StorableModel(file, fileReference, featureRegistry.CreateFeatures(new ItemContext(source, file, fileReference)));
		var folder = new TestModelFactory().CreateModel("folder", "Folder", out _);
		var resolver = new TestBrowseLocationResolver([fileModel])
		{
			LocationModelFactory = _ => folder,
		};

		using var session = new BrowseSessionModel(resolver);
		await session.NavigateAsync(new FolderLocation(folder.Reference));
		await using var preview = new BrowsePreviewModel(session, TimeSpan.Zero);

		session.SetSelection([fileReference.GetKey()], fileReference.GetKey(), null);
		await WaitUntilAsync(() => preview.Current.Status is BrowsePreviewStatus.Ready);

		var result = preview.Current.Result as StreamPreviewResult;
		Assert.IsNotNull(result);
		Assert.AreEqual(fileReference.GetKey(), preview.Current.TargetKey);
		Assert.AreEqual("text/plain", result!.ContentType);
		Assert.AreEqual(file.Name, result.SuggestedFileName);
		Assert.AreEqual(FileAccess.Read, file.LastAccess);
		Assert.AreEqual("preview content", await ReadTextAsync(result.Content));

		await preview.DisposeAsync();
		Assert.AreEqual(1, openedStream.DisposeCount);
}

	private static StreamPreviewLoader CreateLoader(TestPolicy? policy = null)
	{
		return new StreamPreviewLoader(new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".txt", "text/plain"), ]), policy ?? new TestPolicy());
	}

	private static ItemContext CreateContext(TestStorageSource source, IStorable model)
	{
		return new ItemContext(source, model, new StorableReference(source.SourceId, model.Id));
	}

	private static async Task<string> ReadTextAsync(Stream stream)
	{
		stream.Position = 0;
		using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

		return await reader.ReadToEndAsync();
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
			{
				Assert.Fail("The expected preview state was not reached.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class TestPolicy : IPreviewStreamAccessPolicy
	{
		public PreviewBlockReason? BlockReason { get; init; }

		public PreviewRequest? Request { get; private set; }

		public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
		{
			Request = request;
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(BlockReason);
		}
	}

	private sealed class TestFile : IFile, IStorageAddressSource
	{
		private readonly Func<CancellationToken, Task<Stream>> openStream;

		public string Id { get; }

		public string Name { get; }

		public StorageAddress Address { get; }

		public int OpenCount { get; private set; }

		public FileAccess? LastAccess { get; private set; }

		public TestFile(string id, string name, Func<CancellationToken, Task<Stream>> openStream, string? addressValue = null)
		{
			Id = id;
			Name = name;
			this.openStream = openStream;
			Address = new StorageAddress("test", addressValue ?? name);
		}

		public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken)
		{
			OpenCount++;
			LastAccess = accessMode;

			return await openStream(cancellationToken);
		}
	}

	private sealed class TrackingMemoryStream : MemoryStream
	{
		private int disposed;

		public int DisposeCount { get; private set; }

		public TrackingMemoryStream(byte[] content)
			: base(content, writable: false)
		{
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && Interlocked.Exchange(ref disposed, 1) is 0)
			{
				DisposeCount++;
			}

			base.Dispose(disposing);
		}
	}

	private sealed class NonSeekableStream : Stream
	{
		private readonly byte[] content;

		private int position;

		private int disposed;

		public int DisposeCount { get; private set; }

		public Exception? ReadException { get; init; }

		public Action? BeforeRead { get; init; }

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public NonSeekableStream(byte[] content)
		{
			this.content = content;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			BeforeRead?.Invoke();
			if (ReadException is not null)
			{
				throw ReadException;
			}

			var available = Math.Min(count, content.Length - position);
			content.AsSpan(position, available).CopyTo(buffer.AsSpan(offset, available));
			position += available;

			return available;
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			BeforeRead?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(Read(buffer.Span));
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Flush() { }

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing && Interlocked.Exchange(ref disposed, 1) is 0)
			{
				DisposeCount++;
			}

			base.Dispose(disposing);
		}

		private int Read(Span<byte> buffer)
		{
			if (ReadException is not null)
			{
				throw ReadException;
			}

			var available = Math.Min(buffer.Length, content.Length - position);
			content.AsSpan(position, available).CopyTo(buffer[..available]);
			position += available;

			return available;
		}
	}
}

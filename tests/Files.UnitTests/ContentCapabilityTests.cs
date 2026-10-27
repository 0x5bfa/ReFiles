// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Previews;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;
using Files.Core.Windows;

namespace Files.UnitTests;

/// <summary>Tests content discovery, composition, access failures, and ownership independently of file classification.</summary>
[TestClass]
public sealed class ContentCapabilityTests
{
	/// <summary>Files without read access do not gain previews or archive access from their type or address.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task FileClassificationAndAddressDoNotGrantReadAccess()
	{
		await using var source = new TestStorageSource();
		var file = new ClassifiedFile("archive.zip");
		var loader = CreatePreviewLoader();
		var registry = new CapabilityBuilder().Add<IArchiveSource>(new ArchiveSourceFactory()).Add<IPreviewSource>(new PreviewSourceFactory(loader)).Build();
		await using var model = new StorableModelFactory(registry).Create(source, file);

		Assert.IsInstanceOfType<IFileModel>(model);
		Assert.IsNull(model.Get<IReadableContent>());
		Assert.IsNull(model.Get<IPreviewSource>());
		Assert.IsNull(model.Get<IArchiveSource>());
		var result = await new SevenZipArchiveBackend().TryMountAsync(new ArchiveMountRequest(source, model));
		Assert.IsInstanceOfType<ArchiveMountResult.Unsupported>(result);
	}

	/// <summary>Preview resolves registered and wrapped read access and borrows the capability until model disposal.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task PreviewUsesComposedReadAccessAndOwnsOnlyTheReturnedStream()
	{
		await using var source = new TestStorageSource();
		var reader = new TestContent(() => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("preview"))));
		var wrappedReadCount = 0;
		var wrapper = new TestContent(() =>
		{
			wrappedReadCount++;

			return reader.OpenReadAsync();
		});
		var registry = new CapabilityBuilder()
			.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => reader))
			.AddWrapper<IReadableContent>(new DelegateCapabilityWrapper<IReadableContent>((_, _) => wrapper))
			.Add<IPreviewSource>(new PreviewSourceFactory(CreatePreviewLoader()))
			.Build();
		await using var model = new StorableModelFactory(registry).Create(source, new ClassifiedFile("note.txt"));
		var preview = model.Get<IPreviewSource>();
		Assert.IsNotNull(preview);
		var result = await preview.GetPreviewAsync(new PreviewRequest());
		Assert.IsInstanceOfType<StreamPreviewResult>(result);
		var content = ((StreamPreviewResult)result).Content;
		using var textReader = new StreamReader(content, leaveOpen: true);
		Assert.AreEqual("preview", await textReader.ReadToEndAsync());
		Assert.AreEqual(1, wrappedReadCount);
		Assert.IsNull(model.Get<IWritableContent>());

		await result.DisposeAsync();
		Assert.IsFalse(content.CanRead);
		Assert.AreEqual(0, reader.DisposeCount);
		Assert.AreEqual(0, wrapper.DisposeCount);
		await model.DisposeAsync();
		Assert.AreEqual(1, reader.DisposeCount);
		Assert.AreEqual(1, wrapper.DisposeCount);
	}

	/// <summary>Archive opening observes composed read failures without falling back to an address.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ArchiveOpeningUsesTheComposedReaderAndPreservesAccessFailures()
	{
		await using var source = new TestStorageSource();
		var error = new UnauthorizedAccessException("Denied by the composed reader.");
		var reader = new TestContent(() => Task.FromException<Stream>(error));
		var registry = new CapabilityBuilder()
			.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => reader))
			.Add<IArchiveSource>(new ArchiveSourceFactory())
			.Build();
		await using var model = new StorableModelFactory(registry).Create(source, new ClassifiedFile("archive.zip"));

		Assert.IsNotNull(model.Get<IArchiveSource>());
		var result = await new SevenZipArchiveBackend().TryMountAsync(new ArchiveMountRequest(source, model));
		Assert.IsInstanceOfType<ArchiveMountResult.Failed>(result);
		Assert.AreSame(error, ((ArchiveMountResult.Failed)result).Error);
		Assert.AreEqual(1, reader.OpenCount);
	}

	/// <summary>Archive stream setup releases an opened stream when seeking fails or opening completes after cancellation.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task ArchiveStreamSetupDisposesOnFailureAndCancellation()
	{
		await using var source = new TestStorageSource();
		var stream = new SeekFailureStream();
		var reader = new TestContent(() => Task.FromResult<Stream>(stream));
		var registry = new CapabilityBuilder().Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => reader)).Build();
		await using var model = new StorableModelFactory(registry).Create(source, new ClassifiedFile("archive.zip"));
		var result = await new SevenZipArchiveBackend().TryMountAsync(new ArchiveMountRequest(source, model));

		Assert.IsInstanceOfType<ArchiveMountResult.Failed>(result);
		Assert.AreEqual(1, stream.DisposeCount);

		using var cancellation = new CancellationTokenSource();
		var cancelledStream = new MemoryStream();
		var cancelledReader = new TestContent(() =>
		{
			cancellation.Cancel();

			return Task.FromResult<Stream>(cancelledStream);
		});
		var cancelledRegistry = new CapabilityBuilder().Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => cancelledReader)).Build();
		await using var cancelledModel = new StorableModelFactory(cancelledRegistry).Create(source, new ClassifiedFile("cancelled.zip"));
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await new SevenZipArchiveBackend().TryMountAsync(new ArchiveMountRequest(source, cancelledModel), cancellation.Token));
		Assert.IsFalse(cancelledStream.CanRead);
	}

	/// <summary>Capability collections created from the same context keep their dependencies independent.</summary>
	[TestMethod]
	public void ComposedDependenciesRemainScopedToTheirCapabilityCollection()
	{
		var source = new TestStorageSource();
		var file = new ClassifiedFile("archive.zip");
		var context = new ItemContext(source, file, new StorableReference(source.SourceId, file.Id));
		var reader = new TestContent(() => Task.FromResult<Stream>(new MemoryStream()));
		using var withRead = new CapabilityBuilder()
			.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(_ => reader))
			.Add<IArchiveSource>(new ArchiveSourceFactory())
			.Build().CreateCapabilities(context);
		using var withoutRead = new CapabilityBuilder().Add<IArchiveSource>(new ArchiveSourceFactory()).Build().CreateCapabilities(context);

		Assert.IsNotNull(withRead.Get<IArchiveSource>());
		Assert.IsNull(withoutRead.Get<IArchiveSource>());
		Assert.IsNull(context.GetCapability<IReadableContent>());
	}

	/// <summary>Circular content dependencies fail predictably without poisoning later resolution attempts.</summary>
	[TestMethod]
	public void CircularContentDependenciesAreRejected()
	{
		var source = new TestStorageSource();
		var file = new ClassifiedFile("archive.zip");
		var registry = new CapabilityBuilder()
			.Add<IReadableContent>(new DelegateCapabilityFactory<IReadableContent>(context =>
			{
				context.GetCapability<IArchiveSource>();

				return null;
			}))
			.Add<IArchiveSource>(new ArchiveSourceFactory())
			.Build();
		using var model = new StorableModelFactory(registry).Create(source, file);

		Assert.Throws<InvalidOperationException>(() => model.Get<IArchiveSource>());
		Assert.Throws<InvalidOperationException>(() => model.Get<IArchiveSource>());
	}

	/// <summary>Windows content access is composed for files and leaves ordinary folders without stream capabilities.</summary>
	/// <returns>The asynchronous test.</returns>
	[TestMethod]
	public async Task WindowsFilesExposeReadAndWriteWithoutGivingOrdinaryFoldersStreams()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"Files.ContentCapabilityTests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "content.txt");
		await File.WriteAllTextAsync(path, "abc");
		try
		{
			await using var source = new WindowsStorageSource();
			var contentFactory = new WindowsContentSourceFactory();
			var registry = new CapabilityBuilder().Add<IReadableContent>(contentFactory).Add<IWritableContent>(contentFactory).Build();
			var models = new StorableModelFactory(registry);
			await using var folder = models.Create(source, await source.ResolveAsync(new StorageAddress("file", directory)));
			await using var file = models.Create(source, await source.ResolveAsync(new StorageAddress("file", path)));

			Assert.IsNull(folder.Get<IReadableContent>());
			Assert.IsNull(folder.Get<IWritableContent>());
			var writer = file.Get<IWritableContent>();
			Assert.IsNotNull(writer);
			await using (var stream = await writer.OpenWriteAsync())
			{
				await stream.WriteAsync(Encoding.UTF8.GetBytes("x"));
			}

			var reader = file.Get<IReadableContent>();
			Assert.IsNotNull(reader);
			await using var input = await reader.OpenReadAsync();
			using var text = new StreamReader(input);
			Assert.AreEqual("xbc", await text.ReadToEndAsync());
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static StreamPreviewLoader CreatePreviewLoader()
	{
		return new StreamPreviewLoader(new ExtensionPreviewContentTypeResolver([new KeyValuePair<string, string>(".txt", "text/plain")]), AllowPreviewStreamAccessPolicy.Instance);
	}

	private sealed class ClassifiedFile(string name) : IFile, IStorageAddressSource
	{
		public string Id => Name;

		public string Name { get; } = name;

		public StorageAddress Address { get; } = new("file", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", name));
	}

	private sealed class TestContent(Func<Task<Stream>> open) : IReadableContent, IDisposable
	{
		private readonly Func<Task<Stream>> _open = open;

		public int OpenCount { get; private set; }

		public int DisposeCount { get; private set; }

		public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			OpenCount++;

			return _open();
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}

	private sealed class SeekFailureStream : MemoryStream
	{
		public int DisposeCount { get; private set; }

		public override long Position
		{
			get => base.Position;
			set => throw new IOException("Seeking failed.");
		}

		protected override void Dispose(bool disposing)
		{
			DisposeCount++;
			base.Dispose(disposing);
		}
	}
}

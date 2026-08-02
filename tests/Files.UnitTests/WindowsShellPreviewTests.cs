// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Previews;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Windows.Win32;

namespace Files.UnitTests;

[TestClass]
public sealed class WindowsShellPreviewTests
{
	[TestMethod]
	public async Task HandlerResolverCachesPositiveAndNegativeAssociations()
	{
		var handlerClsid = Guid.NewGuid();
		var association = new FakeAssociation
		{
			Value = handlerClsid.ToString("B"),
		};
		var resolver = new WindowsPreviewHandlerResolver(association);
		var source = new TestStorageSource();
		var file = new FakeWindowsFile("item", "document.PDF");
		var context = CreateContext(source, file);

		Assert.AreEqual(handlerClsid, await resolver.ResolveAsync(context));
		Assert.AreEqual(handlerClsid, await resolver.ResolveAsync(context));
		Assert.AreEqual(1, association.CallCount);
		Assert.AreEqual(".PDF", association.Extensions.Single());

		resolver.ClearCache();
		Assert.AreEqual(handlerClsid, await resolver.ResolveAsync(context));
		Assert.AreEqual(2, association.CallCount);

		var missing = new FakeWindowsFile("missing", "document.unknown");
		association.Value = null;
		Assert.IsNull(await resolver.ResolveAsync(CreateContext(source, missing)));
		Assert.IsNull(await resolver.ResolveAsync(CreateContext(source, missing)));
		Assert.AreEqual(3, association.CallCount);
	}

	[TestMethod]
	public async Task HandlerResolverUsesTheFileSystemNameWhenTheDisplayNameHasNoExtension()
	{
		var expected = Guid.NewGuid();
		var association = new FakeAssociation
		{
			Value = expected.ToString("B"),
		};
		var resolver = new WindowsPreviewHandlerResolver(association);
		var source = new TestStorageSource();
		var file = new FakeWindowsFile("item", "Document", @"C:\Content\document.pdf");

		var actual = await resolver.ResolveAsync(CreateContext(source, file));

		Assert.AreEqual(expected, actual);
		Assert.AreEqual(".PDF", association.Extensions.Single());
	}

	[TestMethod]
	public async Task HandlerResolverTreatsMalformedGuidAsUnavailable()
	{
		var association = new FakeAssociation { Value = "not-a-guid" };
		var resolver = new WindowsPreviewHandlerResolver(association);
		var source = new TestStorageSource();

		var result = await resolver.ResolveAsync(CreateContext(source, new FakeWindowsFile("item", "document.pdf")));

		Assert.IsNull(result);
	}

	[TestMethod]
	public async Task ShellLoaderReturnsDescriptorOrBlockedResultWithoutActivation()
	{
		var handlerClsid = Guid.NewGuid();
		var resolver = new FakeHandlerResolver { HandlerClsid = handlerClsid };
		var policy = new FakeShellPolicy();
		var loader = new WindowsShellPreviewLoader(resolver, policy);
		var source = new TestStorageSource();
		var file = new FakeWindowsFile("item", "document.pdf");
		var context = CreateContext(source, file);

		Assert.IsTrue(loader.CanLoad(context));
		var result = await loader.GetPreviewAsync(new PreviewRequest(), context);

		var descriptor = result as WindowsShellPreviewResult;
		Assert.IsNotNull(descriptor);
		Assert.AreEqual(context.Reference, descriptor!.Reference);
		Assert.AreEqual(handlerClsid, descriptor.HandlerClsid);
		Assert.AreEqual(0, file.OpenCount);
		Assert.AreEqual(1, resolver.ActivationCount);

		policy.BlockReason = PreviewBlockReason.DisabledByPolicy;
		var blocked = await loader.GetPreviewAsync(new PreviewRequest(), context);
		Assert.IsInstanceOfType<BlockedPreviewResult>(blocked);
		Assert.AreEqual(0, file.OpenCount);
	}

	[TestMethod]
	public async Task ShellLoaderReturnsNullWhenAssociationIsMissing()
	{
		var loader = new WindowsShellPreviewLoader(new FakeHandlerResolver(), new FakeShellPolicy());
		var source = new TestStorageSource();
		var file = new FakeWindowsFile("item", "document.pdf");

		var result = await loader.GetPreviewAsync(new PreviewRequest(), CreateContext(source, file));

		Assert.IsNull(result);
		Assert.AreEqual(0, file.OpenCount);
}

	[TestMethod]
	public void PreviewHostValidatesBoundsAndWindowHandle()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsPreviewBounds(0, 0, -1, 10));
		Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsPreviewBounds(0, 0, 10, -1));
		Assert.Throws<ArgumentException>(() => new WindowsPreviewHost(0, new WindowsPreviewBounds(0, 0, 1, 1)));

		var desktop = PInvoke.GetDesktopWindow();
		var host = new WindowsPreviewHost((desktop), new WindowsPreviewBounds(0, 0, 640, 480));

		Assert.AreNotEqual(0, host.WindowHandle);
	}

	[TestMethod]
	public void PreviewTargetRejectsIdentityMismatch()
	{
		var source = new TestStorageSource();
		var item = new FakeWindowsFile("actual", "document.pdf");
		var requestedReference = new StorableReference(source.SourceId, "requested");
		var model = new StorableModel(item, requestedReference, ItemFeatureRegistry.Empty.CreateFeatures(new ItemContext(source, item, requestedReference)));

		Assert.Throws<InvalidDataException>(() => new WindowsPreviewTarget(model, item));

		model.Dispose();
	}

	[TestMethod]
	public async Task SessionUsesStreamItemFileOrderAndCleansUpIdempotently()
	{
		var order = new List<string>();
		var controller = new FakeController(order)
		{
			StreamResult = true,
			ItemResult = true,
			FileResult = true,
		};
		var target = CreateTarget("item", "document.pdf");
		var scheduler = new InlineScheduler();
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), scheduler, new FakeControllerFactory(controller));

		await using var session = await factory.CreateAsync(CreateResult(target.Reference), CreateHost());
		var concreteSession = (WindowsShellPreviewSession)session;
		Assert.AreEqual(WindowsShellPreviewSessionState.Previewing, concreteSession.State);

		CollectionAssert.AreEqual(new[] {"site", "stream", "window", "bounds", "preview"}, order.ToArray());

		await session.SetBoundsAsync(new WindowsPreviewBounds(1, 2, 3, 4));
		await session.SetThemeAsync(new WindowsPreviewColor(1, 2, 3), new WindowsPreviewColor(4, 5, 6));
		await session.SetFocusAsync();
		Assert.AreEqual((nint)123, await session.QueryFocusAsync());
		Assert.IsTrue(await session.TryTranslateAcceleratorAsync((nint)1));

		await session.DisposeAsync();
		Assert.AreEqual(WindowsShellPreviewSessionState.Disposed, concreteSession.State);
		Assert.AreEqual(1, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Model.CoreModel).IsDisposed);
}

	[TestMethod]
	public async Task SessionFallsBackFromStreamToItemThenFile()
	{
		var order = new List<string>();
		var controller = new FakeController(order)
		{
			StreamResult = false,
			ItemResult = false,
			FileResult = true,
		};
		var target = CreateTarget("item", "document.pdf");
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller));

		var session = await factory.CreateAsync(CreateResult(target.Reference), CreateHost());
		await session.DisposeAsync();

		CollectionAssert.AreEqual(new[] {"site", "stream", "item", "file", "window", "bounds", "preview", "dispose"}, order.ToArray());
}

	[TestMethod]
	public async Task SessionFailureCleansControllerAndResolvedTarget()
	{
		var order = new List<string>();
		var controller = new FakeController(order)
		{
			StreamResult = true,
			ThrowOnPreview = true,
		};
		var target = CreateTarget("item", "document.pdf");
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller));

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await factory.CreateAsync(CreateResult(target.Reference), CreateHost()));

		Assert.AreEqual(1, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Model.CoreModel).IsDisposed);
}

	[TestMethod]
	public async Task CompositionPrefersStreamAndFallsBackToShellDescriptor()
	{
		var source = new TestStorageSource();
		var handlerResolver = new FakeHandlerResolver { HandlerClsid = Guid.NewGuid() };
		var streamLoader = new StreamPreviewLoader(new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".txt", "text/plain"), ]), new AllowPreviewPolicy());
		var shellLoader = new WindowsShellPreviewLoader(handlerResolver, new FakeShellPolicy());
		var featureRegistry = new ItemFeatureBuilder()
			.Add<IPreviewSource>(new PreviewSourceFactory(streamLoader), priority: 200)
			.Add<IPreviewSource>(new PreviewSourceFactory(shellLoader), priority: 100)
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner())
			.Build();

		var textFile = new FakeWindowsFile("text", "readme.txt")
		{
			StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("stream"))),
		};
		var textReference = new StorableReference(source.SourceId, textFile.Id);
		var textModel = new StorableModel(textFile, textReference, featureRegistry.CreateFeatures(new ItemContext(source, textFile, textReference)));
		await using var streamResult = await textModel
			.Get<IPreviewSource>()!
			.GetPreviewAsync(new PreviewRequest());
		Assert.IsInstanceOfType<StreamPreviewResult>(streamResult);
		textModel.Dispose();

		var shellFile = new FakeWindowsFile("shell", "document.pdf");
		var shellReference = new StorableReference(source.SourceId, shellFile.Id);
		var shellModel = new StorableModel(shellFile, shellReference, featureRegistry.CreateFeatures(new ItemContext(source, shellFile, shellReference)));
		await using var shellResult = await shellModel
			.Get<IPreviewSource>()!
			.GetPreviewAsync(new PreviewRequest());
		Assert.IsInstanceOfType<WindowsShellPreviewResult>(shellResult);
		shellModel.Dispose();
}

	private static ItemContext CreateContext(TestStorageSource source, IStorable model)
	{
		return new ItemContext(source, model, new StorableReference(source.SourceId, model.Id));
	}

	private static WindowsShellPreviewResult CreateResult(StorableReference reference)
		=> new(reference, Guid.NewGuid());

	private static WindowsPreviewHost CreateHost()
		=> new(PInvoke.GetDesktopWindow(), new WindowsPreviewBounds(0, 0, 640, 480));

	private static WindowsPreviewTarget CreateTarget(string id, string name)
	{
		var source = new TestStorageSource();
		var item = new FakeWindowsFile(id, name);
		var reference = new StorableReference(source.SourceId, item.Id);
		var model = new StorableModel(item, reference, ItemFeatureRegistry.Empty.CreateFeatures(new ItemContext(source, item, reference)));

		return new WindowsPreviewTarget(model, item);
	}

	private sealed class FakeAssociation : IWindowsPreviewHandlerAssociation
	{
		public string? Value { get; set; }

		public int CallCount { get; private set; }

		public IList<string> Extensions { get; } = [];

		public string? QueryPreviewHandler(string normalizedExtension)
		{
			CallCount++;
			Extensions.Add(normalizedExtension);

			return Value;
		}
	}

	private sealed class FakeHandlerResolver : IWindowsPreviewHandlerResolver
	{
		public Guid? HandlerClsid { get; init; }

		public int ActivationCount { get; private set; }

		public ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default)
		{
			ActivationCount++;
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(HandlerClsid);
		}
	}

	private sealed class FakeShellPolicy : IWindowsShellPreviewPolicy
	{
		public PreviewBlockReason? BlockReason { get; set; }

		public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
			=> BlockReason;
	}

	private sealed class AllowPreviewPolicy : IPreviewStreamAccessPolicy
	{
		public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<PreviewBlockReason?>(null);
	}

	private sealed class FakeTargetResolver : IWindowsPreviewTargetResolver
	{
		private WindowsPreviewTarget target;

		public FakeTargetResolver(WindowsPreviewTarget target)
		{
			this.target = target;
		}

		public ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var resolved = target;
			target = null!;

			return ValueTask.FromResult(resolved);
		}
	}

	private sealed class FakeControllerFactory : IWindowsPreviewHandlerControllerFactory
	{
		private readonly IWindowsPreviewHandlerController controller;

		public FakeControllerFactory(IWindowsPreviewHandlerController controller)
		{
			this.controller = controller;
		}

		public IWindowsPreviewHandlerController Create(Guid handlerClsid)
			=> controller;
	}

	private sealed class FakeController : IWindowsPreviewHandlerController
	{
		private readonly IList<string> order;

		public bool StreamResult { get; init; }

		public bool ItemResult { get; init; }

		public bool FileResult { get; init; }

		public bool ThrowOnPreview { get; init; }

		public int DisposeCount { get; private set; }

		public FakeController(IList<string> order)
		{
			this.order = order;
		}

		public void SetSite() => order.Add("site");

		public bool TryInitializeWithStream(string fileSystemPath)
		{
			order.Add("stream");

			return StreamResult;
		}

		public bool TryInitializeWithItem(string parsingName)
		{
			order.Add("item");

			return ItemResult;
		}

		public bool TryInitializeWithFile(string fileSystemPath)
		{
			order.Add("file");

			return FileResult;
		}

		public void SetWindow(nint windowHandle, WindowsPreviewBounds bounds)
			=> order.Add("window");

		public void SetBounds(WindowsPreviewBounds bounds)
			=> order.Add("bounds");

		public void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground)
			=> order.Add("theme");

		public void DoPreview()
		{
			order.Add("preview");
			if (ThrowOnPreview)
			{
				throw new InvalidOperationException("preview failed");
			}
		}

		public void SetFocus() => order.Add("focus");

		public nint QueryFocus() => 123;

		public bool TryTranslateAccelerator(nint messagePointer)
		{
			order.Add("translate");

			return messagePointer != 0;
		}

		public void Dispose()
		{
			DisposeCount++;
			order.Add("dispose");
		}
	}

	private sealed class InlineScheduler : IWindowsShellScheduler
	{
		public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
			=> Invoke(action, cancellationToken);

		public Task<T> InvokeConcurrentAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
			=> Invoke(action, cancellationToken);

		public Task<T> InvokeOperationAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
			=> Invoke(action, cancellationToken);

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		private static Task<T> Invoke<T>(Func<T> action, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return Task.FromResult(action());
		}
	}

	private sealed class FakeWindowsFile : IWindowsStorable, IFile, IDisposable
	{
		public string Id { get; }

		public string Name { get; }

		public StorageAddress Address { get; }

		public string ParsingName => Address.Value;

		public string? FileSystemPath => Address.Value;

		public bool IsFileSystem => true;

		public bool IsStream => true;

		public int OpenCount { get; private set; }

		public Func<CancellationToken, Task<Stream>> StreamFactory { get; init; } =
			_ => Task.FromResult<Stream>(new MemoryStream());

		public bool IsDisposed { get; private set; }

		public FakeWindowsFile(string id, string name, string? fileSystemPath = null)
		{
			Id = id;
			Name = name;
			Address = new StorageAddress("file", fileSystemPath ?? name);
		}

		public Task<IFolder> GetParentAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IFolder>(null!);

		public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
		{
			OpenCount++;

			return await StreamFactory(cancellationToken);
		}

		public void Dispose() => IsDisposed = true;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for windows shell preview behavior.
/// </summary>
[TestClass]
public sealed class WindowsShellPreviewTests
{
	/// <summary>Test case: default handler activation uses an out-of-process server with cloaking.</summary>
	[TestMethod]
	public void DefaultActivationPolicyUsesLocalServerWithCloaking()
	{
		var policy = new LocalServerWindowsPreviewHandlerActivationPolicy();

		Assert.AreEqual(WindowsPreviewHandlerActivationContext.LocalServer | WindowsPreviewHandlerActivationContext.EnableCloaking, policy.GetContext(Guid.NewGuid()));
	}

	/// <summary>Verifies the exact registry value forms that opt a handler out of low-integrity isolation.</summary>
	[TestMethod]
	public void LowIntegrityOptOutAcceptsOnlyFourByteNonzeroDwordOrBinaryValues()
	{
		Assert.IsTrue(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled(1, RegistryValueKind.DWord));
		Assert.IsTrue(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled(-1, RegistryValueKind.DWord));
		Assert.IsTrue(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled(new byte[] { 1, 0, 0, 0 }, RegistryValueKind.Binary));
		Assert.IsFalse(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled(0, RegistryValueKind.DWord));
		Assert.IsFalse(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled(new byte[] { 1 }, RegistryValueKind.Binary));
		Assert.IsFalse(WindowsPreviewHandlerIsolationPolicy.IsLowIntegrityDisabled("1", RegistryValueKind.String));
	}

	/// <summary>
	/// Test case: handler resolver caches positive and negative associations.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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

	/// <summary>
	/// Test case: handler resolver uses the file system name when the display name has no extension.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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

	/// <summary>
	/// Test case: handler resolver treats malformed guid as unavailable.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task HandlerResolverTreatsMalformedGuidAsUnavailable()
	{
		var association = new FakeAssociation { Value = "not-a-guid" };
		var resolver = new WindowsPreviewHandlerResolver(association);
		var source = new TestStorageSource();

		var result = await resolver.ResolveAsync(CreateContext(source, new FakeWindowsFile("item", "document.pdf")));

		Assert.IsNull(result);
	}

	/// <summary>
	/// Test case: shell loader returns descriptor or blocked result without activation.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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

	/// <summary>
	/// Test case: shell loader returns null when association is missing.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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

	/// <summary>
	/// Test case: preview host validates bounds and window handle.
	/// </summary>
	[TestMethod]
	public void PreviewHostValidatesBoundsAndWindowHandle()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsPreviewBounds(0, 0, -1, 10));
		Assert.Throws<ArgumentOutOfRangeException>(() => new WindowsPreviewBounds(0, 0, 10, -1));
		Assert.Throws<ArgumentException>(() => new WindowsPreviewHost(HWND.Null, new WindowsPreviewBounds(0, 0, 1, 1)));

		var desktop = PInvoke.GetDesktopWindow();
		var host = new WindowsPreviewHost(desktop, new WindowsPreviewBounds(0, 0, 640, 480));

		Assert.IsFalse(host.WindowHandle.IsNull);
	}

	/// <summary>
	/// Test case: preview target rejects identity mismatch.
	/// </summary>
	[TestMethod]
	public void PreviewTargetRejectsIdentityMismatch()
	{
		var source = new TestStorageSource();
		var item = new FakeWindowsFile("actual", "document.pdf");
		var requestedReference = new StorableReference(source.SourceId, "requested");
		var model = new StorableModel(item, requestedReference, CapabilityRegistry.Empty.CreateCapabilities(new ItemContext(source, item, requestedReference)));

		Assert.Throws<InvalidDataException>(() => new WindowsPreviewTarget(model, item));

		model.Dispose();
	}

	/// <summary>
	/// Test case: session uses stream item file order and cleans up idempotently.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		var factory = new WindowsShellPreviewSessionFactory(
			new FakeTargetResolver(target), scheduler, new FakeControllerFactory(controller), AllowWindowsShellPreviewPolicy.Instance, AllowHandlerRegistrationValidator.Instance);

		await using var session = await factory.CreateAsync(CreateResult(target.Reference), CreateHost());
		var concreteSession = (WindowsShellPreviewSession)session;
		Assert.AreEqual(WindowsShellPreviewSessionState.Previewing, concreteSession.State);

		CollectionAssert.AreEqual(new[] {"stream", "site", "window", "visuals", "preview"}, order.ToArray());

		await session.SetBoundsAsync(new WindowsPreviewBounds(1, 2, 3, 4));
		await session.SetThemeAsync(new WindowsPreviewColor(1, 2, 3), new WindowsPreviewColor(4, 5, 6));
		await session.SetFocusAsync();
		Assert.AreEqual((HWND)(nint)123, await session.QueryFocusAsync());
		MSG message = default;
		message.message = 1;
		Assert.IsTrue(await session.TryTranslateAcceleratorAsync(in message));

		await session.DisposeAsync();
		Assert.AreEqual(WindowsShellPreviewSessionState.Disposed, concreteSession.State);
		Assert.AreEqual(1, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
}

	/// <summary>
	/// Test case: session falls back from stream to item then file.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		var factory = new WindowsShellPreviewSessionFactory(
			new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller), AllowWindowsShellPreviewPolicy.Instance, AllowHandlerRegistrationValidator.Instance);

		var session = await factory.CreateAsync(CreateResult(target.Reference), CreateHost());
		await session.DisposeAsync();

		CollectionAssert.AreEqual(new[] {"stream", "item", "file", "site", "window", "visuals", "preview", "dispose"}, order.ToArray());
}

	/// <summary>
	/// Test case: session does not site a handler when every initializer fails.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SessionDoesNotSetSiteWhenEveryInitializerFails()
	{
		var order = new List<string>();
		var controller = new FakeController(order);
		var target = CreateTarget("item", "document.pdf");
		var factory = new WindowsShellPreviewSessionFactory(
			new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller), AllowWindowsShellPreviewPolicy.Instance, AllowHandlerRegistrationValidator.Instance);

		await Assert.ThrowsAsync<NotSupportedException>(async () => await factory.CreateAsync(CreateResult(target.Reference), CreateHost()));

		CollectionAssert.AreEqual(new[] {"stream", "item", "file", "dispose"}, order.ToArray());
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
	}

	/// <summary>
	/// Test case: session creation revalidates policy on the preview STA immediately before activating the handler.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SessionRevalidatesPolicyOnPreviewStaBeforeHandlerActivation()
	{
		var order = new List<string>();
		var controller = new FakeController(order) { StreamResult = true };
		var target = CreateTarget("item", "document.pdf", includeContext: true);
		var request = new PreviewRequest(42, PreviewHydrationPolicy.AllowHydration);
		var policy = new FakeShellPolicy { BlockReasonAfterFirstCheck = PreviewBlockReason.DisabledByPolicy };
		var controllerFactory = new FakeControllerFactory(controller);
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), new InlineScheduler(), controllerFactory, policy, AllowHandlerRegistrationValidator.Instance);

		var exception = await Assert.ThrowsAsync<WindowsShellPreviewBlockedException>(async () =>
			await factory.CreateAsync(new WindowsShellPreviewResult(target.Reference, Guid.NewGuid(), request), CreateHost()));

		Assert.AreEqual(PreviewBlockReason.DisabledByPolicy, exception.Reason);
		Assert.AreEqual(2, policy.CallCount);
		Assert.AreSame(request, policy.Request);
		Assert.AreEqual(0, controllerFactory.CreateCount);
		Assert.AreEqual(0, order.Count);
		Assert.AreEqual(0, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
	}

	/// <summary>
	/// Test case: a Shell retry authorization cannot move to a different resolved target before controller activation.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task AuthorizedShellResultRejectsDifferentResolvedTargetBeforeControllerActivation()
	{
		await using var authorizedTarget = CreateTarget("item", "document.pdf", fileSystemPath: @"C:\authorized\document.pdf");
		var resolvedTarget = CreateTarget("item", "document.pdf", fileSystemPath: @"C:\replacement\document.pdf");
		var request = new PreviewRequest(42, PreviewHydrationPolicy.LocalOnly, new PreviewTrustAuthorization(authorizedTarget.Context!));
		var result = new WindowsShellPreviewResult(authorizedTarget.Reference, Guid.NewGuid(), request);
		var controller = new FakeController([]) { StreamResult = true };
		var controllerFactory = new FakeControllerFactory(controller);
		var factory = new WindowsShellPreviewSessionFactory(
			new FakeTargetResolver(resolvedTarget), new InlineScheduler(), controllerFactory, new TargetBoundShellPolicy(), AllowHandlerRegistrationValidator.Instance);

		var exception = await Assert.ThrowsAsync<WindowsShellPreviewBlockedException>(async () => await factory.CreateAsync(result, CreateHost()));

		Assert.AreEqual(PreviewBlockReason.Untrusted, exception.Reason);
		Assert.AreEqual(0, controllerFactory.CreateCount);
		Assert.AreEqual(0, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)resolvedTarget.Item).IsDisposed);
	}

	/// <summary>
	/// Test case: session creation rejects a handler whose current registration no longer matches the result.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SessionRejectsHandlerThatIsNoLongerRegisteredForTheItem()
	{
		var order = new List<string>();
		var controller = new FakeController(order) { StreamResult = true };
		var target = CreateTarget("item", "document.pdf", includeContext: true);
		var validator = new FixedHandlerRegistrationValidator(false);
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller), AllowWindowsShellPreviewPolicy.Instance, validator);

		var exception = await Assert.ThrowsAsync<WindowsShellPreviewBlockedException>(async () => await factory.CreateAsync(CreateResult(target.Reference), CreateHost()));

		Assert.AreEqual(PreviewBlockReason.DisabledByPolicy, exception.Reason);
		Assert.AreEqual(1, validator.CallCount);
		Assert.AreEqual(0, order.Count);
		Assert.AreEqual(0, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
	}

	/// <summary>
	/// Test case: a target without policy context is rejected before activating the handler.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task SessionRejectsTargetWithoutPolicyContext()
	{
		var order = new List<string>();
		var controller = new FakeController(order) { StreamResult = true };
		var target = CreateTarget("item", "document.pdf", includeContext: false);
		var factory = new WindowsShellPreviewSessionFactory(new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller));

		var exception = await Assert.ThrowsAsync<WindowsShellPreviewBlockedException>(async () => await factory.CreateAsync(CreateResult(target.Reference), CreateHost()));

		Assert.AreEqual(PreviewBlockReason.DisabledByPolicy, exception.Reason);
		Assert.AreEqual(0, order.Count);
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
	}

	/// <summary>
	/// Test case: session failure cleans controller and resolved target.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
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
		var factory = new WindowsShellPreviewSessionFactory(
			new FakeTargetResolver(target), new InlineScheduler(), new FakeControllerFactory(controller), AllowWindowsShellPreviewPolicy.Instance, AllowHandlerRegistrationValidator.Instance);

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await factory.CreateAsync(CreateResult(target.Reference), CreateHost()));

		Assert.AreEqual(1, controller.DisposeCount);
		Assert.IsTrue(((FakeWindowsFile)target.Item).IsDisposed);
}

	/// <summary>
	/// Test case: composition prefers stream and falls back to shell descriptor.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task CompositionPrefersStreamAndFallsBackToShellDescriptor()
	{
		var source = new TestStorageSource();
		var handlerResolver = new FakeHandlerResolver { HandlerClsid = Guid.NewGuid() };
		var streamLoader = new StreamPreviewLoader(new ExtensionPreviewContentTypeResolver([ new KeyValuePair<string, string>(".txt", "text/plain"), ]), new AllowPreviewPolicy());
		var shellLoader = new WindowsShellPreviewLoader(handlerResolver, new FakeShellPolicy());
		var capabilityRegistry = new CapabilityBuilder()
			.Add<IPreviewSource>(new PreviewSourceFactory(streamLoader), priority: 200)
			.Add<IPreviewSource>(new PreviewSourceFactory(shellLoader), priority: 100)
			.SetCombiner<IPreviewSource>(new PreviewSourceCombiner())
			.Build();

		var textFile = new FakeWindowsFile("text", "readme.txt")
		{
			StreamFactory = _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("stream"))),
		};
		var textReference = new StorableReference(source.SourceId, textFile.Id);
		var textModel = new StorableModel(textFile, textReference, capabilityRegistry.CreateCapabilities(new ItemContext(source, textFile, textReference)));
		await using var streamResult = await textModel
			.Get<IPreviewSource>()!
			.GetPreviewAsync(new PreviewRequest());
		Assert.IsInstanceOfType<StreamPreviewResult>(streamResult);
		textModel.Dispose();

		var shellFile = new FakeWindowsFile("shell", "document.pdf");
		var shellReference = new StorableReference(source.SourceId, shellFile.Id);
		var shellModel = new StorableModel(shellFile, shellReference, capabilityRegistry.CreateCapabilities(new ItemContext(source, shellFile, shellReference)));
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

	private static WindowsPreviewTarget CreateTarget(string id, string name, bool includeContext = true, string? fileSystemPath = null)
	{
		var source = new TestStorageSource();
		var item = new FakeWindowsFile(id, name, fileSystemPath);
		var reference = new StorableReference(source.SourceId, item.Id);
		var context = new ItemContext(source, item, reference);
		var model = new StorableModel(item, reference, CapabilityRegistry.Empty.CreateCapabilities(context));

		return new WindowsPreviewTarget(model, item, includeContext ? context : null);
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

		public PreviewBlockReason? BlockReasonAfterFirstCheck { get; set; }

		public int CallCount { get; private set; }

		public PreviewRequest? Request { get; private set; }

		public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
		{
			CallCount++;

			return BlockReason;
		}

		public PreviewBlockReason? GetBlockReason(PreviewRequest request, ItemContext context, Guid handlerClsid)
		{
			Request = request;
			CallCount++;

			return CallCount > 1 ? BlockReasonAfterFirstCheck ?? BlockReason : BlockReason;
		}

		public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(GetBlockReason(request, context, handlerClsid));
		}
	}

	private sealed class TargetBoundShellPolicy : IWindowsShellPreviewPolicy
	{
		public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid) => PreviewBlockReason.Untrusted;

		public PreviewBlockReason? GetBlockReason(PreviewRequest request, ItemContext context, Guid handlerClsid)
		{
			return request.TrustAuthorization?.AppliesTo(context) is true ? null : PreviewBlockReason.Untrusted;
		}

		public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return ValueTask.FromResult(GetBlockReason(request, context, handlerClsid));
		}
	}

	private sealed class AllowPreviewPolicy : IPreviewStreamAccessPolicy
	{
		public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<PreviewBlockReason?>(null);
	}

	private sealed class FakeTargetResolver : IWindowsPreviewTargetResolver
	{
		private WindowsPreviewTarget _target;

		public FakeTargetResolver(WindowsPreviewTarget target)
		{
			_target = target;
		}

		public ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var resolved = _target;
			_target = null!;

			return ValueTask.FromResult(resolved);
		}
	}

	private sealed class FakeControllerFactory : IWindowsPreviewHandlerControllerFactory
	{
		private readonly IWindowsPreviewHandlerController _controller;

		public int CreateCount { get; private set; }

		public FakeControllerFactory(IWindowsPreviewHandlerController controller)
		{
			_controller = controller;
		}

		public IWindowsPreviewHandlerController Create(Guid handlerClsid)
		{
			CreateCount++;

			return _controller;
		}
	}

	private sealed class AllowHandlerRegistrationValidator : IWindowsPreviewHandlerRegistrationValidator
	{
		public static AllowHandlerRegistrationValidator Instance { get; } = new();

		public bool IsCurrentHandler(ItemContext context, Guid handlerClsid) => true;
	}

	private sealed class FixedHandlerRegistrationValidator : IWindowsPreviewHandlerRegistrationValidator
	{
		private readonly bool _isCurrent;

		public int CallCount { get; private set; }

		public FixedHandlerRegistrationValidator(bool isCurrent)
		{
			_isCurrent = isCurrent;
		}

		public bool IsCurrentHandler(ItemContext context, Guid handlerClsid)
		{
			CallCount++;

			return _isCurrent;
		}
	}

	private sealed class FakeController : IWindowsPreviewHandlerController
	{
		private readonly IList<string> _order;

		public bool StreamResult { get; init; }

		public bool ItemResult { get; init; }

		public bool FileResult { get; init; }

		public bool ThrowOnPreview { get; init; }

		public int DisposeCount { get; private set; }

		public FakeController(IList<string> order)
		{
			_order = order;
		}

		public void SetSite() => _order.Add("site");

		public bool TryInitializeWithStream(string fileSystemPath)
		{
			_order.Add("stream");

			return StreamResult;
		}

		public bool TryInitializeWithItem(string parsingName)
		{
			_order.Add("item");

			return ItemResult;
		}

		public bool TryInitializeWithFile(string fileSystemPath)
		{
			_order.Add("file");

			return FileResult;
		}

		public void SetWindow(HWND windowHandle, WindowsPreviewBounds bounds)
			=> _order.Add("window");

		public void SetBounds(WindowsPreviewBounds bounds)
			=> _order.Add("bounds");

		public void SetTheme(WindowsPreviewColor background, WindowsPreviewColor foreground)
			=> _order.Add("theme");

		public void ApplySystemVisuals() => _order.Add("visuals");

		public void DoPreview()
		{
			_order.Add("preview");
			if (ThrowOnPreview)
			{
				throw new InvalidOperationException("preview failed");
			}
		}

		public void SetFocus() => _order.Add("focus");

		public HWND QueryFocus() => (HWND)(nint)123;

		public bool TryTranslateAccelerator(in MSG message)
		{
			_order.Add("translate");

			return message.message != 0;
		}

		public void Dispose()
		{
			DisposeCount++;
			_order.Add("dispose");
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

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IFolder?>(null);

		public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
		{
			OpenCount++;

			return await StreamFactory(cancellationToken);
		}

		public void Dispose() => IsDisposed = true;
	}
}

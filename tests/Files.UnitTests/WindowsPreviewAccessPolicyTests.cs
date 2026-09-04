// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.Urlmon;

namespace Files.UnitTests;

/// <summary>Tests the Windows preview access policy.</summary>
[TestClass]
public sealed class WindowsPreviewAccessPolicyTests
{
	private static readonly Guid _handlerClsid = new("00000000-0000-0000-0000-000000000123");

	/// <summary>Verifies that Shell policy evaluation receives the complete preview request asynchronously.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ShellLoaderPassesFullRequestToAsyncPolicy()
	{
		var context = CreateWindowsContext();
		var request = new PreviewRequest(42, PreviewHydrationPolicy.AllowHydration);
		var policy = new RecordingShellPolicy();
		var loader = new WindowsShellPreviewLoader(new FixedHandlerResolver(), policy);

		var result = await loader.GetPreviewAsync(request, context);

		var shellResult = Assert.IsInstanceOfType<WindowsShellPreviewResult>(result);

		Assert.AreSame(request, policy.Request);
		Assert.AreSame(request, shellResult.Request);
		Assert.AreEqual(_handlerClsid, policy.HandlerClsid);
	}

	/// <summary>Verifies that files allowed by the Windows URL policy are allowed by both stream and Shell previews.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task AllowedUrlPolicyIsAllowedForStreamAndShell()
	{
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Allowed));
		var context = CreateWindowsContext();
		var request = new PreviewRequest(20);

		Assert.IsNull(await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(request, context));
		Assert.IsNull(await ((IWindowsShellPreviewPolicy)policy).GetBlockReasonAsync(request, context, _handlerClsid));
	}

	/// <summary>Verifies that a Windows URL policy denial blocks preview.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task BlockedUrlPolicyBlocksPreview()
	{
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked));

		var reason = await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), CreateWindowsContext());

		Assert.AreEqual(PreviewBlockReason.Untrusted, reason);
	}

	/// <summary>Verifies that attributes indicating unavailable content block local-only preview.</summary>
	/// <param name="attributes">The file attributes to evaluate.</param>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	[DataRow(0x00001000u)]
	[DataRow(0x00040000u)]
	[DataRow(0x00400000u)]
	public async Task LocalOnlyBlocksUnavailableContent(uint attributes)
	{
		var trustResolver = new FixedTrustResolver(new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Allowed));
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(attributes, 10), trustResolver: trustResolver);

		var reason = await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), CreateWindowsContext());

		Assert.AreEqual(PreviewBlockReason.RequiresHydration, reason);
		Assert.AreEqual(0, trustResolver.CallCount);
	}

	/// <summary>Verifies that the byte limit is enforced before the trust resolver accesses zone metadata.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task MaximumBytesBlocksBeforeZoneAccess()
	{
		var trustResolver = new FixedTrustResolver(new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Allowed));
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 101), trustResolver: trustResolver);

		var reason = await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(new PreviewRequest(100), CreateWindowsContext());

		Assert.AreEqual(PreviewBlockReason.TooLarge, reason);
		Assert.AreEqual(0, trustResolver.CallCount);
	}

	/// <summary>Verifies that indeterminate file metadata and URL policy results fail closed.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task IndeterminateMetadataAndZoneFailClosed()
	{
		var context = CreateWindowsContext();
		var metadataPolicy = CreatePolicy(null, new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Allowed));
		var trustPolicy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate));

		Assert.AreEqual(PreviewBlockReason.AccessDenied, await ((IPreviewStreamAccessPolicy)metadataPolicy).GetBlockReasonAsync(new PreviewRequest(), context));
		Assert.AreEqual(PreviewBlockReason.Untrusted, await ((IPreviewStreamAccessPolicy)trustPolicy).GetBlockReasonAsync(new PreviewRequest(), context));
	}

	/// <summary>Verifies that a handler registration exception bypasses only the Shell URL policy gate.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task HandlerTrustExceptionBypassesOnlyShellUrlPolicyGate()
	{
		var trustResolver = new FixedTrustResolver(new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked));
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), trustResolver: trustResolver, allowsUntrusted: true);
		var context = CreateWindowsContext();

		Assert.IsNull(await ((IWindowsShellPreviewPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), context, _handlerClsid));
		Assert.AreEqual(PreviewBlockReason.Untrusted, await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), context));
		Assert.AreEqual(1, trustResolver.CallCount);
	}

	/// <summary>Verifies that a handler registration exception does not bypass enterprise protection.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task HandlerTrustExceptionDoesNotBypassEnterpriseProtection()
	{
		var trustResolver = new FixedTrustResolver(new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked));
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), trustResolver: trustResolver, allowsUntrusted: true, enterpriseProtected: true);

		var reason = await ((IWindowsShellPreviewPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), CreateWindowsContext(), _handlerClsid);

		Assert.AreEqual(PreviewBlockReason.Untrusted, reason);
		Assert.AreEqual(0, trustResolver.CallCount);
	}

	/// <summary>Verifies that an explicit Explorer-style retry bypasses trust checks but not resource limits.</summary>
	[TestMethod]
	public void UserOverrideBypassesTrustAfterResourceChecks()
	{
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 101), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked), enterpriseProtected: true);
		var context = CreateWindowsContext();
		var authorization = new PreviewTrustAuthorization(context);

		Assert.IsNull(policy.GetBlockReason(new PreviewRequest(200, PreviewHydrationPolicy.LocalOnly, authorization), context, _handlerClsid));
		Assert.AreEqual(PreviewBlockReason.TooLarge, policy.GetBlockReason(new PreviewRequest(100, PreviewHydrationPolicy.LocalOnly, authorization), context, _handlerClsid));
	}

	/// <summary>Verifies that copying an explicit retry request cannot transfer its trust authorization to a different target.</summary>
	[TestMethod]
	public void UserOverrideIsBoundToTheAuthorizedTarget()
	{
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked));
		var authorizedContext = CreateWindowsContext();
		var request = new PreviewRequest(20, PreviewHydrationPolicy.LocalOnly, new PreviewTrustAuthorization(authorizedContext));
		var copiedRequest = request with { };
		var sameReferenceAtAnotherPath = CreateWindowsContext("item", @"C:\other.txt");
		var anotherReferenceAtTheSamePath = CreateWindowsContext("other", @"C:\item.txt");

		Assert.AreEqual(PreviewBlockReason.Untrusted, policy.GetBlockReason(copiedRequest, sameReferenceAtAnotherPath, _handlerClsid));
		Assert.AreEqual(PreviewBlockReason.Untrusted, policy.GetBlockReason(copiedRequest, anotherReferenceAtTheSamePath, _handlerClsid));
	}

	/// <summary>Verifies that activation can re-resolve the authorized Windows target without transferring authorization to another path.</summary>
	[TestMethod]
	public void UserOverrideAcceptsTheSameReResolvedWindowsTarget()
	{
		var policy = CreatePolicy(new WindowsPreviewFileMetadata(0, 10), new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked));
		var authorizedContext = CreateWindowsContext();
		var resolvedContext = CreateWindowsContext();
		var request = new PreviewRequest(20, PreviewHydrationPolicy.LocalOnly, new PreviewTrustAuthorization(authorizedContext));

		Assert.IsNull(policy.GetBlockReason(request, resolvedContext, _handlerClsid));
	}

	/// <summary>Verifies that a non-success URL policy result cannot be mistaken for the allow value in a zeroed buffer.</summary>
	[TestMethod]
	public void UrlPolicySFalseWithZeroPolicyIsIndeterminate()
	{
		var result = WindowsPreviewUrlTrustResolver.InterpretUrlPolicy(HRESULT.S_FALSE, new byte[sizeof(uint)]);

		Assert.AreEqual(WindowsPreviewTrustStatus.Indeterminate, result.Status);
	}

	/// <summary>Verifies that ZoneCheck reports a successful policy denial through S_FALSE.</summary>
	[TestMethod]
	public void ZoneCheckSFalseWithDisallowPolicyIsBlocked()
	{
		var result = WindowsPreviewUrlTrustResolver.InterpretZoneCheckPolicy(HRESULT.S_FALSE, 3);

		Assert.AreEqual(WindowsPreviewTrustStatus.Blocked, result.Status);
	}

	/// <summary>Verifies that the Explorer-compatible ZoneCheck path requires an exact allow value.</summary>
	[TestMethod]
	public void ZoneCheckPolicyWithAdditionalFlagsIsBlocked()
	{
		var result = WindowsPreviewUrlTrustResolver.InterpretZoneCheckPolicy(HRESULT.S_OK, 0x00010000);

		Assert.AreEqual(WindowsPreviewTrustStatus.Blocked, result.Status);
	}

	/// <summary>Verifies that the Windows-specific default leaves non-Windows stream items unchanged.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task NonWindowsStreamItemIsNotBlocked()
	{
		var policy = CreatePolicy(null, new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate));
		var source = new TestStorageSource();
		var item = new TestStorable("item", "item.txt");
		var context = new ItemContext(source, item, new StorableReference(source.SourceId, item.Id));

		Assert.IsNull(await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(new PreviewRequest(), context));
	}

	/// <summary>Verifies that the real Windows policy blocks an Internet-zone alternate data stream and allows local or unblocked files.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task ZoneIdentifierControlsInitialPreviewTrustDecision()
	{
		var directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"Files.UnitTests-PreviewZone-{Guid.NewGuid():N}"));
		var localPath = Path.Combine(directoryPath, "local.txt");
		var internetPath = Path.Combine(directoryPath, "internet.txt");
		var unblockedPath = Path.Combine(directoryPath, "unblocked.txt");
		try
		{
			var rootPath = Path.GetPathRoot(directoryPath);
			if (string.IsNullOrWhiteSpace(rootPath) || !string.Equals(new DriveInfo(rootPath).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
			{
				Assert.Inconclusive("The Zone.Identifier integration test requires NTFS.");
			}

			Directory.CreateDirectory(directoryPath);
			await File.WriteAllTextAsync(localPath, "local");
			await File.WriteAllTextAsync(internetPath, "internet");
			await File.WriteAllTextAsync(unblockedPath, "unblocked");
			try
			{
				WriteZoneIdentifier(internetPath);
				WriteZoneIdentifier(unblockedPath);
				File.Delete(GetZoneIdentifierPath(unblockedPath));
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
			{
				Assert.Inconclusive($"The test volume does not support Zone.Identifier streams: {error.Message}");
			}

			if (!UsesExpectedPreviewZonePolicy(localPath, internetPath))
			{
				Assert.Inconclusive("The installed Windows policy does not block Internet-zone previews while allowing local files.");
			}

			var policy = new WindowsPreviewAccessPolicy();
			var request = new PreviewRequest();
			var internetContext = CreateWindowsContext("internet", internetPath);

			Assert.IsNull(await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(request, CreateWindowsContext("local", localPath)));
			Assert.AreEqual(PreviewBlockReason.Untrusted, await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(request, internetContext));
			Assert.AreEqual(PreviewBlockReason.Untrusted, await ((IWindowsShellPreviewPolicy)policy).GetBlockReasonAsync(request, internetContext, _handlerClsid));
			Assert.IsNull(await ((IPreviewStreamAccessPolicy)policy).GetBlockReasonAsync(request, CreateWindowsContext("unblocked", unblockedPath)));
		}
		finally
		{
			DeleteIfExists(localPath);
			DeleteIfExists(internetPath);
			DeleteIfExists(unblockedPath);
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath);
			}
		}
	}

	private static WindowsPreviewAccessPolicy CreatePolicy(
		WindowsPreviewFileMetadata? metadata,
		WindowsPreviewTrustResult? trust = null,
		FixedTrustResolver? trustResolver = null,
		bool allowsUntrusted = false,
		bool enterpriseProtected = false)
	{
		return new WindowsPreviewAccessPolicy(
			new FixedMetadataResolver(metadata),
			trustResolver ?? new FixedTrustResolver(trust ?? new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate)),
			new FixedHandlerTrustResolver(allowsUntrusted),
			new FixedEnterpriseIdResolver(enterpriseProtected));
	}

	private static ItemContext CreateWindowsContext(string id = "item", string fileSystemPath = @"C:\item.txt")
	{
		var source = new TestStorageSource();
		var item = new FakeWindowsFile(id, fileSystemPath);

		return new ItemContext(source, item, new StorableReference(source.SourceId, item.Id));
	}

	private static string GetZoneIdentifierPath(string filePath) => $"{filePath}:Zone.Identifier";

	private static bool UsesExpectedPreviewZonePolicy(string localPath, string internetPath)
	{
		try
		{
			var localHr = PInvoke.ZoneCheckUrlExCache(new Uri(localPath).AbsoluteUri, out var localPolicy, sizeof(uint), 0, 0, PInvoke.URLACTION_SHELL_PREVIEW, (uint)PUAF.PUAF_NOUI, null, 0);
			var internetHr = PInvoke.ZoneCheckUrlExCache(new Uri(internetPath).AbsoluteUri, out var internetPolicy, sizeof(uint), 0, 0, PInvoke.URLACTION_SHELL_PREVIEW, (uint)PUAF.PUAF_NOUI, null, 0);

			return localHr.Succeeded && localPolicy == PInvoke.URLPOLICY_ALLOW && internetHr.Succeeded && internetPolicy != PInvoke.URLPOLICY_ALLOW;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
	}

	private static void WriteZoneIdentifier(string filePath)
	{
		var zoneIdentifierPath = GetZoneIdentifierPath(filePath);
		File.WriteAllText(zoneIdentifierPath, "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/preview-test\r\n");
		StringAssert.Contains(File.ReadAllText(zoneIdentifierPath), "ZoneId=3");
	}

	private static void DeleteIfExists(string filePath)
	{
		if (File.Exists(filePath))
		{
			File.Delete(filePath);
		}
	}

	private sealed class FixedMetadataResolver : IWindowsPreviewFileMetadataResolver
	{
		private readonly WindowsPreviewFileMetadata? _metadata;

		public FixedMetadataResolver(WindowsPreviewFileMetadata? metadata)
		{
			_metadata = metadata;
		}

		public WindowsPreviewFileMetadata? GetMetadata(ItemContext context)
		{
			return _metadata;
		}
	}

	private sealed class FixedTrustResolver : IWindowsPreviewTrustResolver
	{
		private readonly WindowsPreviewTrustResult _trust;

		public int CallCount { get; private set; }

		public FixedTrustResolver(WindowsPreviewTrustResult trust)
		{
			_trust = trust;
		}

		public WindowsPreviewTrustResult GetTrust(ItemContext context)
		{
			CallCount++;

			return _trust;
		}
	}

	private sealed class FixedHandlerTrustResolver : IWindowsPreviewHandlerTrustResolver
	{
		private readonly bool _allowsUntrusted;

		public FixedHandlerTrustResolver(bool allowsUntrusted)
		{
			_allowsUntrusted = allowsUntrusted;
		}

		public bool AllowsUntrustedPreviews(Guid handlerClsid) => _allowsUntrusted;
	}

	private sealed class FixedEnterpriseIdResolver : IWindowsPreviewEnterpriseIdResolver
	{
		private readonly bool _hasEnterpriseId;

		public FixedEnterpriseIdResolver(bool hasEnterpriseId)
		{
			_hasEnterpriseId = hasEnterpriseId;
		}

		public bool HasEnterpriseId(ItemContext context) => _hasEnterpriseId;
	}

	private sealed class FixedHandlerResolver : IWindowsPreviewHandlerResolver
	{
		public ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult<Guid?>(_handlerClsid);
		}
	}

	private sealed class RecordingShellPolicy : IWindowsShellPreviewPolicy
	{
		public PreviewRequest? Request { get; private set; }

		public Guid HandlerClsid { get; private set; }

		public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
		{
			throw new AssertFailedException("The synchronous compatibility member must not be used by the loader.");
		}

		public async ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();

			Request = request;
			HandlerClsid = handlerClsid;

			return null;
		}
	}

	private sealed class FakeWindowsFile : IWindowsStorable, IFile
	{
		public string Id { get; }

		public string Name => Path.GetFileName(FileSystemPath);

		public StorageAddress Address => new("file", FileSystemPath);

		public string ParsingName => Address.Value;

		public string FileSystemPath { get; }

		public bool IsFileSystem => true;

		public bool IsStream => true;

		public FakeWindowsFile(string id, string fileSystemPath)
		{
			Id = id;
			FileSystemPath = fileSystemPath;
		}

		public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default) => Task.FromResult<IFolder?>(null);

		public Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default) => throw new AssertFailedException("Policy evaluation must not open content.");
	}
}

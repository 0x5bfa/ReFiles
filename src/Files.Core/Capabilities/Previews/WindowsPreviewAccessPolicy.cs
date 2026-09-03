// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Files.Core.Capabilities;
using Files.Core.Storage.Windows;
using Microsoft.Win32;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.Urlmon;
using Windows.Win32.UI.Shell;

namespace Files.Core.Capabilities.Previews;

/// <summary>Applies Windows file hydration, size, and trust checks before preview content is accessed.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPreviewAccessPolicy : IPreviewStreamAccessPolicy, IWindowsShellPreviewPolicy
{
	private const uint FileAttributeOffline = 0x00001000;
	private const uint FileAttributeRecallOnOpen = 0x00040000;
	private const uint FileAttributeRecallOnDataAccess = 0x00400000;
	private const uint HydrationFileAttributes = FileAttributeOffline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess;

	private readonly IWindowsPreviewFileMetadataResolver _metadataResolver;
	private readonly IWindowsPreviewTrustResolver _trustResolver;
	private readonly IWindowsPreviewHandlerTrustResolver _handlerTrustResolver;
	private readonly IWindowsPreviewEnterpriseIdResolver _enterpriseIdResolver;

	/// <summary>Initializes a Windows preview access policy using operating-system metadata and trust resolvers.</summary>
	public WindowsPreviewAccessPolicy()
		: this(new WindowsPreviewFileMetadataResolver(), new WindowsPreviewUrlTrustResolver(), new WindowsPreviewHandlerTrustResolver(), new WindowsPreviewEnterpriseIdResolver())
	{
	}

	internal WindowsPreviewAccessPolicy(
		IWindowsPreviewFileMetadataResolver metadataResolver,
		IWindowsPreviewTrustResolver trustResolver,
		IWindowsPreviewHandlerTrustResolver handlerTrustResolver,
		IWindowsPreviewEnterpriseIdResolver enterpriseIdResolver)
	{
		ArgumentNullException.ThrowIfNull(metadataResolver);
		ArgumentNullException.ThrowIfNull(trustResolver);
		ArgumentNullException.ThrowIfNull(handlerTrustResolver);
		ArgumentNullException.ThrowIfNull(enterpriseIdResolver);

		_metadataResolver = metadataResolver;
		_trustResolver = trustResolver;
		_handlerTrustResolver = handlerTrustResolver;
		_enterpriseIdResolver = enterpriseIdResolver;
	}

	/// <summary>Gets a conservative blocking reason without request-specific Windows trust checks.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see cref="PreviewBlockReason.DisabledByPolicy"/> for Windows files, or <see langword="null"/> for items outside this Windows-specific policy.</returns>
	public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return IsWindowsFile(context) ? PreviewBlockReason.DisabledByPolicy : null;
	}

	/// <summary>Gets the reason a Windows Shell preview handler is blocked.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed or the item is not Windows-backed.</returns>
	public PreviewBlockReason? GetBlockReason(PreviewRequest request, ItemContext context, Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return GetBlockReasonCore(request, context, handlerClsid);
	}

	/// <summary>Gets the reason a stream preview is blocked for a Windows-backed file.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when access is allowed or the item is not Windows-backed.</returns>
	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult(GetBlockReasonCore(request, context, null));
	}

	/// <summary>Gets the reason a Windows Shell preview handler is blocked.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed or the item is not Windows-backed.</returns>
	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult(GetBlockReason(request, context, handlerClsid));
	}

	private PreviewBlockReason? GetBlockReasonCore(PreviewRequest request, ItemContext context, Guid? handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);

		if (!IsWindowsFile(context))
		{
			return null;
		}

		var metadata = _metadataResolver.GetMetadata(context);

		if (metadata is null || metadata.Value.Length < 0)
		{
			return PreviewBlockReason.AccessDenied;
		}

		if (request.HydrationPolicy is PreviewHydrationPolicy.LocalOnly && (metadata.Value.Attributes & HydrationFileAttributes) != 0)
		{
			return PreviewBlockReason.RequiresHydration;
		}

		if (request.MaximumBytes is long maximumBytes && metadata.Value.Length > maximumBytes)
		{
			return PreviewBlockReason.TooLarge;
		}

		if (request.TrustAuthorization?.AppliesTo(context) is true)
		{
			return null;
		}

		var bypassUrlPolicy = handlerClsid is Guid clsid && _handlerTrustResolver.AllowsUntrustedPreviews(clsid);
		if (!bypassUrlPolicy && _trustResolver.GetTrust(context).Status is not WindowsPreviewTrustStatus.Allowed)
		{
			return PreviewBlockReason.Untrusted;
		}

		if (_enterpriseIdResolver.HasEnterpriseId(context))
		{
			return PreviewBlockReason.Untrusted;
		}

		return null;
	}

	private static bool IsWindowsFile(ItemContext context)
	{
		return context.CoreModel is IWindowsStorable and IFile;
	}
}

internal interface IWindowsPreviewFileMetadataResolver
{
	WindowsPreviewFileMetadata? GetMetadata(ItemContext context);
}

internal interface IWindowsPreviewTrustResolver
{
	WindowsPreviewTrustResult GetTrust(ItemContext context);
}

internal interface IWindowsPreviewHandlerTrustResolver
{
	bool AllowsUntrustedPreviews(Guid handlerClsid);
}

internal interface IWindowsPreviewEnterpriseIdResolver
{
	bool HasEnterpriseId(ItemContext context);
}

internal readonly record struct WindowsPreviewFileMetadata(uint Attributes, long Length);

internal enum WindowsPreviewTrustStatus
{
	Allowed,
	Blocked,
	Indeterminate,
}

internal readonly record struct WindowsPreviewTrustResult(WindowsPreviewTrustStatus Status);

internal sealed class WindowsPreviewFileMetadataResolver : IWindowsPreviewFileMetadataResolver
{
	private const uint FileAttributeDirectory = 0x00000010;
	public WindowsPreviewFileMetadata? GetMetadata(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.FileSystemPath) || !Path.IsPathFullyQualified(item.FileSystemPath))
		{
			return null;
		}

		try
		{
			var attributes = PInvoke.GetFileAttributes(item.FileSystemPath);
			if (attributes == PInvoke.INVALID_FILE_ATTRIBUTES || (attributes & FileAttributeDirectory) != 0)
			{
				return null;
			}

			var fileInfo = new FileInfo(item.FileSystemPath);
			fileInfo.Refresh();
			if (!fileInfo.Exists)
			{
				return null;
			}

			return new WindowsPreviewFileMetadata(attributes, fileInfo.Length);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
		{
			return null;
		}
	}
}

internal sealed class WindowsPreviewUrlTrustResolver : IWindowsPreviewTrustResolver
{
	public WindowsPreviewTrustResult GetTrust(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.ParsingName))
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		try
		{
			var hr = PInvoke.SHCreateItemFromParsingName(item.ParsingName, null, out IShellItem shellItem);
			if (hr != HRESULT.S_OK)
			{
				return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
			}

			var url = ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_URL);

			return url is null ? new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate) : EvaluateUrlPolicy(url);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or NotSupportedException or SecurityException)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}
	}

	private static WindowsPreviewTrustResult EvaluateUrlPolicy(string url)
	{
		if (PInvoke.CoInternetCreateSecurityManager(null!, out var securityManager, 0) != HRESULT.S_OK)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		var policy = new byte[sizeof(uint)];
		byte context = 0;
		var hr = securityManager.ProcessUrlAction(url, PInvoke.URLACTION_SHELL_PREVIEW, policy, in context, 0, (uint)PUAF.PUAF_NOUI, 0);

		return InterpretUrlPolicy(hr, policy);
	}

	internal static WindowsPreviewTrustResult InterpretUrlPolicy(HRESULT hr, ReadOnlySpan<byte> policy)
	{
		if (hr != HRESULT.S_OK || policy.Length < sizeof(uint))
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		var permissions = BinaryPrimitives.ReadUInt32LittleEndian(policy) & PInvoke.URLPOLICY_MASK_PERMISSIONS;

		return new WindowsPreviewTrustResult(permissions == PInvoke.URLPOLICY_ALLOW ? WindowsPreviewTrustStatus.Allowed : WindowsPreviewTrustStatus.Blocked);
	}
}

internal sealed class WindowsPreviewHandlerTrustResolver : IWindowsPreviewHandlerTrustResolver
{
	private const string AutomaticallyPreviewUntrustedFilesValue = "AutomaticallyPreviewUntrustedFiles";

	public bool AllowsUntrustedPreviews(Guid handlerClsid)
	{
		try
		{
			using var key = Registry.ClassesRoot.OpenSubKey($"CLSID\\{handlerClsid:B}", writable: false);

			return key?.GetValue(AutomaticallyPreviewUntrustedFilesValue) is int value && value == 1;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
		{
			return false;
		}
	}
}

internal sealed class WindowsPreviewEnterpriseIdResolver : IWindowsPreviewEnterpriseIdResolver
{
	public bool HasEnterpriseId(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.ParsingName))
		{
			return false;
		}

		try
		{
			var hr = PInvoke.SHCreateItemFromParsingName(item.ParsingName, null, out IShellItem shellItem);
			if (hr != HRESULT.S_OK || shellItem is not IShellItem2 shellItem2)
			{
				return false;
			}

			return HasEnterpriseId(shellItem2);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or NotSupportedException or SecurityException)
		{
			return false;
		}
	}

	private static unsafe bool HasEnterpriseId(IShellItem2 shellItem)
	{
		var hr = shellItem.GetString(in PInvoke.PKEY_Security_EncryptionOwners, out var enterpriseId);
		if (hr.Failed)
		{
			return false;
		}

		try
		{
			return !string.IsNullOrEmpty(enterpriseId.ToString());
		}
		finally
		{
			PInvoke.CoTaskMemFree(enterpriseId.Value);
		}
	}
}

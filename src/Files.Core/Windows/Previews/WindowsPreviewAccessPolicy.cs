// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;
using Microsoft.Win32;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.Urlmon;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

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

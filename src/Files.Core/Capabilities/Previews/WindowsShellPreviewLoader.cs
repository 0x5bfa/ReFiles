// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.Capabilities;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Loads Windows Shell preview handler results for supported files.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShellPreviewLoader : IPreviewLoader
{
	private readonly IWindowsPreviewHandlerResolver _handlerResolver;
	private readonly IWindowsShellPreviewPolicy _policy;

	/// <summary>Initializes a Windows Shell preview loader.</summary>
	/// <param name="handlerResolver">The preview handler resolver.</param>
	/// <param name="policy">The Shell preview policy.</param>
	public WindowsShellPreviewLoader(IWindowsPreviewHandlerResolver handlerResolver, IWindowsShellPreviewPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(handlerResolver);
		ArgumentNullException.ThrowIfNull(policy);

		_handlerResolver = handlerResolver;
		_policy = policy;
	}

	/// <summary>Determines whether Windows Shell preview applies to an item.</summary>
	/// <param name="context">The item context.</param>
	/// <returns><see langword="true"/> when the item is a Windows-backed file.</returns>
	public bool CanLoad(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.CoreModel is IWindowsStorable
			&& context.CoreModel is IFile;
	}

	/// <summary>Resolves a Windows Shell preview handler for an item.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A Shell preview result, a blocked result, or <see langword="null"/> when unsupported.</returns>
	public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		if (!CanLoad(context))
		{
			return null;
		}

		var handlerClsid = await _handlerResolver.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		if (handlerClsid is null)
		{
			return null;
		}

		var blockReason = _policy.GetBlockReason(context, handlerClsid.Value);

		return blockReason is not null
			? new BlockedPreviewResult(blockReason.Value)
			: new WindowsShellPreviewResult(context.Reference, handlerClsid.Value);
	}
}

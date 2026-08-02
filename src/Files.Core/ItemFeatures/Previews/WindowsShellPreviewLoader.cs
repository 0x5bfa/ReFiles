// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.Versioning;
using Files.Core.ItemFeatures;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures.Previews;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellPreviewLoader : IPreviewLoader
{
	private readonly IWindowsPreviewHandlerResolver _handlerResolver;
	private readonly IWindowsShellPreviewPolicy _policy;

	public WindowsShellPreviewLoader(IWindowsPreviewHandlerResolver handlerResolver, IWindowsShellPreviewPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(handlerResolver);
		ArgumentNullException.ThrowIfNull(policy);

		_handlerResolver = handlerResolver;
		_policy = policy;
	}

	public bool CanLoad(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.CoreModel is IWindowsStorable
			&& context.CoreModel is IFile;
	}

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

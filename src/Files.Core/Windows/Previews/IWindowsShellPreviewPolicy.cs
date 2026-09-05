// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Capabilities;
using Files.Core.Capabilities.Previews;

namespace Files.Core.Windows;

/// <summary>Controls whether Windows Shell preview handlers may run for an item.</summary>
public interface IWindowsShellPreviewPolicy
{
	/// <summary>Gets the reason a handler is blocked without request-specific limits.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid);

	/// <summary>Gets the reason a handler is blocked for a specific request.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	PreviewBlockReason? GetBlockReason(PreviewRequest request, ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(request);

		return GetBlockReason(context, handlerClsid);
	}

	/// <summary>Gets the reason a handler is blocked.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult(GetBlockReason(request, context, handlerClsid));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Allows registered Shell preview handlers. The default activator still uses a local server.
/// </summary>
public sealed class AllowWindowsShellPreviewPolicy : IWindowsShellPreviewPolicy
{
	/// <summary>Gets the shared policy instance.</summary>
	public static AllowWindowsShellPreviewPolicy Instance { get; } = new();

	private AllowWindowsShellPreviewPolicy()
	{
	}

	/// <summary>Allows the specified preview handler without request-specific limits.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns><see langword="null"/> because the handler is not blocked.</returns>
	public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return null;
	}

	/// <summary>Allows the specified preview handler.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task with no blocking reason.</returns>
	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, Guid handlerClsid, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);

		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<PreviewBlockReason?>(null);
	}
}

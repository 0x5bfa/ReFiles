// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Allows registered Shell preview handlers. The default activator still uses a local server.
/// </summary>
public sealed class AllowWindowsShellPreviewPolicy
	: IWindowsShellPreviewPolicy
{
	public static AllowWindowsShellPreviewPolicy Instance { get; } = new();

	private AllowWindowsShellPreviewPolicy()
	{
	}

	public PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid)
	{
		ArgumentNullException.ThrowIfNull(context);
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		return null;
	}
}

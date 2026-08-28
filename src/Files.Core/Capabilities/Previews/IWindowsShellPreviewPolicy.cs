// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>Controls whether Windows Shell preview handlers may run for an item.</summary>
public interface IWindowsShellPreviewPolicy
{
	/// <summary>Gets the reason a handler is blocked.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="handlerClsid">The preview handler CLSID.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when the handler is allowed.</returns>
	PreviewBlockReason? GetBlockReason(ItemContext context, Guid handlerClsid);
}

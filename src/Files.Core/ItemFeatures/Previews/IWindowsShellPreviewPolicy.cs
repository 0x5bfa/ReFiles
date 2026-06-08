// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsShellPreviewPolicy
{
	PreviewBlockReason? GetBlockReason(
		ItemContext context,
		Guid handlerClsid);
}

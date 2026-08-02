// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsPreviewHandlerResolver
{
	ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default);
}

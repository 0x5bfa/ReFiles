// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>Resolves the Windows preview handler registered for an item.</summary>
public interface IWindowsPreviewHandlerResolver
{
	/// <summary>Resolves a preview handler CLSID.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The handler CLSID, or <see langword="null"/> when none is available.</returns>
	ValueTask<Guid?> ResolveAsync(ItemContext context, CancellationToken cancellationToken = default);
}

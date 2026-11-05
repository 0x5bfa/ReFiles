// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Loads previews for supported items.
/// </summary>
public interface IPreviewLoader
{
	/// <summary>Determines whether this loader supports the item.</summary>
	/// <param name="context">The item context.</param>
	/// <returns><see langword="true"/> when this loader can produce a preview.</returns>
	bool CanLoad(ItemContext context);

	/// <summary>Loads a preview for an item.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The preview result, or <see langword="null"/> when no preview is available.</returns>
	ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default);
}

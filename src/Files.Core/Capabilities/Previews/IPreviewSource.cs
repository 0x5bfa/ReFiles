// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Produces UI-neutral preview content for one item.
/// </summary>
public interface IPreviewSource
{
	/// <summary>Gets preview content for the bound item.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The preview result, or <see langword="null"/> when no preview is available.</returns>
	ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, CancellationToken cancellationToken = default);
}

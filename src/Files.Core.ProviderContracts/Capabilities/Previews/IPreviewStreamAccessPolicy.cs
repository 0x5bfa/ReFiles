// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>Controls whether a stream preview may access item content.</summary>
public interface IPreviewStreamAccessPolicy
{
	/// <summary>Gets the reason a stream preview is blocked.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The blocking reason, or <see langword="null"/> when access is allowed.</returns>
	ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default);
}

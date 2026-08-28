// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Allows stream previews without applying hydration or trust restrictions.
/// </summary>
public sealed class AllowPreviewStreamAccessPolicy
	: IPreviewStreamAccessPolicy
{
	/// <summary>Gets the shared policy instance.</summary>
	public static AllowPreviewStreamAccessPolicy Instance { get; } = new();

	private AllowPreviewStreamAccessPolicy()
	{
	}

	/// <summary>Allows the requested stream preview.</summary>
	/// <param name="request">The preview request.</param>
	/// <param name="context">The item context.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task with no blocking reason.</returns>
	public ValueTask<PreviewBlockReason?> GetBlockReasonAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<PreviewBlockReason?>(null);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Previews;

namespace Files.Core.Browsing;

/// <summary>Provides the current preview state for a browse session.</summary>
public interface IBrowsePreviewModel : IAsyncDisposable
{
	/// <summary>Gets the latest preview snapshot.</summary>
	BrowsePreviewSnapshot Current { get; }

	/// <summary>Occurs when the current preview snapshot changes.</summary>
	event EventHandler? Changed;

	/// <summary>Refreshes the preview for the current browse item.</summary>
	/// <param name="hydrationPolicy">The policy controlling remote hydration.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A value task that represents the refresh operation.</returns>
	ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default);
}

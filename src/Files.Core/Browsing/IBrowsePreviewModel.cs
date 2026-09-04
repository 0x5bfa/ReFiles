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

	/// <summary>Retries an untrusted preview when the supplied snapshot is still the exact current selection.</summary>
	/// <param name="blockedSnapshot">The untrusted snapshot for which the user granted access.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A value task that represents the retry operation.</returns>
	ValueTask PreviewUntrustedAsync(BrowsePreviewSnapshot blockedSnapshot, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

	/// <summary>Publishes an activation-time Shell preview policy block when the supplied snapshot is still current.</summary>
	/// <param name="expectedSnapshot">The Shell preview snapshot whose activation was blocked.</param>
	/// <param name="reason">The policy reason reported by activation.</param>
	/// <returns><see langword="true"/> when the block was published; otherwise, <see langword="false"/>.</returns>
	bool TryReportShellPreviewBlocked(BrowsePreviewSnapshot expectedSnapshot, PreviewBlockReason reason) => false;
}

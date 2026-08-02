// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;

namespace Files.Core.Browsing;

public interface IBrowsePreviewModel : IAsyncDisposable
{
	BrowsePreviewSnapshot Current { get; }

	event EventHandler? Changed;

	ValueTask RefreshAsync(PreviewHydrationPolicy hydrationPolicy = PreviewHydrationPolicy.LocalOnly, CancellationToken cancellationToken = default);
}

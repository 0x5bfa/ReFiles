// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>Creates independently owned content for a split pane.</summary>
public interface ISplittablePaneContent
{
	/// <summary>Creates and initializes content with the same application context.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The new content owned by the caller.</returns>
	ValueTask<IPaneContentSession> CreateSplitAsync(CancellationToken cancellationToken = default);
}

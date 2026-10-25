// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>
/// Represents the owned, UI-independent content of a pane session.
/// </summary>
public interface IPaneContentSession : IAsyncDisposable
{
	/// <summary>Initializes restored content before it is published to its owner.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The initialization operation.</returns>
	ValueTask InitializeAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.CompletedTask;
	}
}

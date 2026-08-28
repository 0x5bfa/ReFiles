// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Base type for UI-neutral preview content.
/// </summary>
public abstract class PreviewResult : IAsyncDisposable
{
	/// <summary>Asynchronously releases resources owned by the preview result.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

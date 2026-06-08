// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Base type for UI-neutral preview content.
/// </summary>
public abstract class PreviewResult : IAsyncDisposable
{
	public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

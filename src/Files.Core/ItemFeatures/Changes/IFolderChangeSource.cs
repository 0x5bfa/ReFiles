// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Watches changes for the folder bound to an item feature.
/// The synchronous disposal member is retained so the item feature lifetime
/// can release the source; callers with an async lifetime
/// should prefer <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </summary>
public interface IFolderChangeSource : IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Raised after a Shell change has been converted to a managed folder change.
	/// </summary>
	event EventHandler<FolderChangeEventArgs>? Changed;

	/// <summary>
	/// Raised when the background notification pump cannot continue.
	/// </summary>
	event EventHandler<FolderChangeErrorEventArgs>? Faulted;

	/// <summary>
	/// Starts the native folder subscription and its change pump.
	/// </summary>
	ValueTask StartAsync(
		CancellationToken cancellationToken = default);
}

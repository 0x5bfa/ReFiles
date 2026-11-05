// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Changes;

/// <summary>
/// Watches changes for the folder bound to an item capability.
/// The item model owns the subscription; callers borrow the capability.
/// </summary>
public interface IFolderChangeSource : ICapability
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
	ValueTask StartAsync(CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Exposes cooperative control state for an active storage operation.
/// </summary>
/// <remarks>Implementations must support concurrent reads from storage backend worker threads.</remarks>
public interface IStorageOperationControl
{
	/// <summary>Gets a value indicating whether the operation should remain paused.</summary>
	bool IsPauseRequested { get; }

	/// <summary>Records an actual pause-state transition reported by the storage backend.</summary>
	/// <param name="isPaused"><see langword="true"/> when the backend entered its pause wait; <see langword="false"/> when it left the pause wait.</param>
	void AcknowledgePauseState(bool isPaused);

	/// <summary>Records that the storage backend observed the cancellation request.</summary>
	void AcknowledgeCancellationRequest();
}

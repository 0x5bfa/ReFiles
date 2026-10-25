// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Observes and controls accepted operations independently of presentation and client requests.</summary>
public interface IStorageOperationMonitor
{
	/// <summary>Occurs when a fresh operation snapshot is available.</summary>
	event EventHandler? Changed;

	/// <summary>Gets the latest authoritative operation values.</summary>
	/// <returns>The operation snapshots.</returns>
	IReadOnlyList<StorageOperationSnapshot> GetOperationSnapshots();

	/// <summary>Requests cooperative cancellation.</summary>
	/// <param name="id">The operation identity.</param>
	/// <returns>Whether the request was accepted.</returns>
	bool Cancel(Guid id);

	/// <summary>Requests pause or resume.</summary>
	/// <param name="id">The operation identity.</param>
	/// <param name="paused">Whether to pause.</param>
	/// <returns>Whether the request was accepted.</returns>
	bool SetPaused(Guid id, bool paused);

	/// <summary>Answers the currently observed interruption.</summary>
	/// <param name="id">The operation identity.</param>
	/// <param name="interruptionId">The displayed interruption identity.</param>
	/// <param name="response">The user's response.</param>
	/// <returns>Whether the answer was accepted.</returns>
	bool ResolveInterruption(Guid id, Guid interruptionId, StorageOperationInterruptionResponse response);

	/// <summary>Forgets a terminal operation summary.</summary>
	/// <param name="id">The operation identity.</param>
	/// <returns>Whether the summary was removed.</returns>
	bool Forget(Guid id);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Contains authoritative operation data without graphs, localization, or UI resources.</summary>
/// <param name="Id">The accepted job identity.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="State">The observed execution state.</param>
/// <param name="CompletedItems">The completed item count.</param>
/// <param name="TotalItems">The total item count.</param>
/// <param name="CompletedBytes">The completed byte count, when available.</param>
/// <param name="TotalBytes">The total byte count, when available.</param>
/// <param name="IsByteProgressForWholeOperation">Whether byte counts cover the whole job.</param>
/// <param name="CurrentItemName">The current item name.</param>
/// <param name="DestinationPath">The destination description.</param>
/// <param name="Interruption">The pending decision, if any.</param>
/// <param name="InterruptionId">The identity required to answer the pending decision.</param>
/// <param name="Error">The terminal failure, if any.</param>
/// <param name="CanCancel">Whether cancellation can be requested.</param>
/// <param name="CanPause">Whether pausing can be requested.</param>
/// <param name="IsCancellationRequested">Whether cancellation was requested.</param>
/// <param name="IsCancellationAcknowledged">Whether execution acknowledged cancellation.</param>
/// <param name="StartedAt">The acceptance timestamp.</param>
/// <param name="CompletedAt">The terminal timestamp, when available.</param>
public sealed record StorageOperationSnapshot(
	Guid Id, StorageJobKind Kind, StorageJobState State, int CompletedItems, int TotalItems, long? CompletedBytes, long? TotalBytes, bool IsByteProgressForWholeOperation, string? CurrentItemName,
	string? DestinationPath, StorageOperationInterruption? Interruption, Guid InterruptionId, Exception? Error, bool CanCancel, bool CanPause, bool IsCancellationRequested,
	bool IsCancellationAcknowledged, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

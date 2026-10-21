// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using Files.Core.Storage;

namespace Files.StorageOperations;

internal sealed record StorageOperationStatus(
	Guid Id,
	TrackedStorageOperationKind Kind,
	TrackedStorageOperationState State,
	int CompletedItems,
	int TotalItems,
	string? CurrentItemName,
	string? DestinationPath,
	long? CompletedBytes,
	long? TotalBytes,
	bool IsByteProgressForWholeOperation,
	double? BytesPerSecond,
	TimeSpan? RemainingTime,
	StorageOperationInterruption? Interruption,
	Guid InterruptionId,
	Exception? Error,
	bool CanCancel,
	bool CanPause,
	bool IsCancellationRequested,
	bool IsCancellationAcknowledged,
	DateTimeOffset StartedAt,
	DateTimeOffset? CompletedAt,
	IReadOnlyList<Vector2> SpeedGraphPoints);

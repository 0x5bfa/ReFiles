// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using Files.Core.Storage;

namespace Files.StorageOperations;

internal static class StorageOperationStatusMapper
{
	internal static StorageOperationStatus Convert(BackgroundServer.OperationSummary summary)
	{
		var kind = summary.Kind switch
		{
			"Copy" => TrackedStorageOperationKind.Copy,
			"Move" => TrackedStorageOperationKind.Move,
			"Delete" => TrackedStorageOperationKind.Delete,
			_ => TrackedStorageOperationKind.Other,
		};
		var state = summary.State switch
		{
			BackgroundServer.OperationState.Queued or BackgroundServer.OperationState.Running => TrackedStorageOperationState.Running,
			BackgroundServer.OperationState.Pausing => TrackedStorageOperationState.Pausing,
			BackgroundServer.OperationState.Paused => TrackedStorageOperationState.Paused,
			BackgroundServer.OperationState.Resuming => TrackedStorageOperationState.Resuming,
			BackgroundServer.OperationState.WaitingForUser => TrackedStorageOperationState.WaitingForUser,
			BackgroundServer.OperationState.Succeeded => TrackedStorageOperationState.Succeeded,
			BackgroundServer.OperationState.Canceled => TrackedStorageOperationState.Canceled,
			_ => TrackedStorageOperationState.Failed,
		};
		var data = summary.Interruption;
		var interruption = summary.HasInterruption ? new StorageOperationInterruption((StorageOperationInterruptionKind)data.Kind, (StorageOperationInterruptionResponses)data.AvailableResponses,
			data.ErrorCode, data.ItemName, data.DestinationPath, data.CanApplyToAll) : null;
		var error = summary.State == BackgroundServer.OperationState.Failed ? new COMException(summary.ErrorMessage, summary.ErrorCode) : null;

		return new StorageOperationStatus(summary.Id, kind, state, checked((int)summary.CompletedItems), checked((int)summary.TotalItems), summary.CurrentItemName, summary.DestinationPath,
			summary.HasByteProgress ? summary.CompletedBytes : null, summary.HasByteProgress ? summary.TotalBytes : null, summary.IsByteProgressForWholeOperation, null, null, interruption,
			data.Id, error, IsActiveState(state), summary.CanPause, summary.IsCancellationRequested, summary.IsCancellationAcknowledged, summary.AcceptedAt,
			summary.HasCompletedAt ? summary.CompletedAt : null, []);
	}

	private static bool IsActiveState(TrackedStorageOperationState state)
	{
		return state is not (TrackedStorageOperationState.Succeeded or TrackedStorageOperationState.Failed or TrackedStorageOperationState.Canceled);
	}

}

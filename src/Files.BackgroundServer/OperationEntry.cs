// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage;

namespace Files.BackgroundServer;

internal sealed class OperationEntry
{
	internal OperationSummary Summary;
	internal readonly CancellationTokenSource Cancellation = new();
	internal readonly Dictionary<StorageOperationInterruptionKind, StorageOperationInterruptionResponse> RememberedResponses = [];
	internal Task CancellationTask = Task.CompletedTask;
	internal TaskCompletionSource<StorageOperationInterruptionResponse>? Decision;

	internal OperationEntry(IServerOperation operation)
	{
		Summary = new OperationSummary
		{
			Id = operation.Id,
			Kind = operation.Kind,
			State = OperationState.Queued,
			AcceptedAt = DateTimeOffset.UtcNow,
			HasItemProgress = true,
			TotalItems = operation.ItemCount,
			CanPause = operation.CanPause,
			DestinationPath = operation.DestinationPath ?? string.Empty,
			CurrentItemName = string.Empty,
			ErrorMessage = string.Empty,
		};
		Summary.CompletedAt = Summary.AcceptedAt;
	}
}

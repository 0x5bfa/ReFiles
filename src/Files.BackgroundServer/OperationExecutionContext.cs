// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading;
using System.Threading.Tasks;
using Files.Core.Storage;

namespace Files.BackgroundServer;

internal sealed class OperationExecutionContext(OperationHost host, OperationEntry entry) : IStorageOperationControl
{
	public bool IsPauseRequested => host.IsPauseRequested(entry);

	public void Report(OperationProgress progress)
	{
		host.Report(entry, progress);
	}

	public void SetResult(StorageOperationResult result)
	{
		host.SetResult(entry, result);
	}

	public void AcknowledgePauseState(bool isPaused)
	{
		host.AcknowledgePause(entry, isPaused);
	}

	public void AcknowledgeCancellationRequest()
	{
		host.AcknowledgeCancellation(entry);
	}

	public ValueTask<StorageOperationInterruptionResponse> RequestInterruptionAsync(StorageOperationInterruption interruption, CancellationToken cancellationToken)
	{
		return host.RequestInterruptionAsync(entry, interruption, cancellationToken);
	}
}

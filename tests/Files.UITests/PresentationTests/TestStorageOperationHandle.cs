// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace Files.UITests;

internal sealed class TestStorageOperationHandle : IStorageOperationControl
{
	private readonly TestStorageOperationController _tracker;
	private readonly Guid _id;

	public CancellationToken CancellationToken { get; }

	public bool IsPauseRequested => _tracker.IsPauseRequested(_id);

	internal TestStorageOperationHandle(TestStorageOperationController tracker, Guid id, CancellationToken cancellationToken)
	{
		_tracker = tracker;
		_id = id;
		CancellationToken = cancellationToken;
	}

	public void AcknowledgePauseState(bool isPaused) => _tracker.AcknowledgePauseState(_id, isPaused);

	public void AcknowledgeCancellationRequest() => _tracker.AcknowledgeCancellationRequest(_id);

	public ValueTask<StorageOperationInterruptionResponse> RequestInterruptionAsync(StorageOperationInterruption interruption, CancellationToken cancellationToken)
	{
		return _tracker.RequestInterruptionAsync(_id, interruption, cancellationToken);
	}

	public void Report(int completedItems, string? currentItemName, long? completedBytes = null, long? totalBytes = null, bool isByteProgressForWholeOperation = false)
	{
		_tracker.ReportProgress(_id, completedItems, currentItemName, completedBytes, totalBytes, isByteProgressForWholeOperation);
	}

	public void Complete()
	{
		_tracker.Complete(_id);
	}

	public void Fail(Exception error)
	{
		_tracker.Fail(_id, error);
	}

	public void MarkCanceled()
	{
		_tracker.MarkCanceled(_id);
	}
}

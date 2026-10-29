// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.UITests;

internal sealed class TestStorageOperationController : IOperationStatusSource, IDisposable
{
	private static readonly ILogger<TestStorageOperationController> _logger = NullLogger<TestStorageOperationController>.Instance;

	private readonly Lock _syncRoot = new();
	private readonly Dictionary<Guid, TestStorageOperationState> _operations = [];
	private readonly List<Guid> _operationOrder = [];
	private bool _isDisposed;

	public event EventHandler? Changed;

	public bool Cancel(Guid id)
	{
		return RequestCancellation(id);
	}

	public bool SetPaused(Guid id, bool paused)
	{
		return paused ? RequestPause(id) : RequestResume(id);
	}

	public bool Forget(Guid id)
	{
		return Remove(id);
	}

	public bool ResolveInterruption(Guid id, Guid interruptionId, StorageOperationInterruptionResponse response)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.InterruptionId != interruptionId)
			{
				return false;
			}
		}

		return ResolveInterruption(id, response);
	}

	public TestStorageOperationHandle StartOperation(TrackedStorageOperationKind kind, int totalItems, string? currentItemName, bool canCancel, CancellationToken cancellationToken = default,
		string? destinationPath = null, bool canPause = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalItems);

		if (kind is not TrackedStorageOperationKind.Copy and not TrackedStorageOperationKind.Move and not TrackedStorageOperationKind.Delete)
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var id = Guid.NewGuid();
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				operationCancellation.Dispose();

				throw new ObjectDisposedException(nameof(TestStorageOperationController));
			}

			_operations.Add(id, new TestStorageOperationState(id, kind, totalItems, currentItemName, destinationPath, canCancel, canPause, operationCancellation));
			_operationOrder.Insert(0, id);
		}

		OnChanged();

		return new TestStorageOperationHandle(this, id, operationCancellation.Token);
	}

	public IReadOnlyList<StorageOperationStatus> GetOperations()
	{
		lock (_syncRoot)
		{
			return _operationOrder.Select(id => _operations[id].CreateStatus()).ToArray();
		}
	}

	public bool RequestCancellation(Guid id)
	{
		CancellationTokenSource? cancellation;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State) || !operation.CanCancel || operation.IsCancellationRequested)
			{
				return false;
			}

			operation.IsCancellationRequested = true;
			cancellation = operation.Cancellation;
		}

		try
		{
			cancellation?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (AggregateException)
		{
		}

		OnChanged();

		return true;
	}

	public bool ResolveInterruption(Guid id, StorageOperationInterruptionResponse response)
	{
		TaskCompletionSource<StorageOperationInterruptionResponse>? responseSource;
		CancellationTokenSource? cancellation = null;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not TrackedStorageOperationState.WaitingForUser || operation.Interruption is null
				|| operation.InterruptionResponseSource is null || !IsDecisionAvailable(operation.Interruption, response.Decision))
			{
				return false;
			}

			if (response.ApplyToAll && operation.Interruption.CanApplyToAll)
			{
				operation.RememberInterruptionResponse(operation.Interruption.Kind, response);
			}

			responseSource = operation.InterruptionResponseSource;
			operation.ResumeFromInterruption();
			if (response.Decision is StorageOperationInterruptionDecision.Cancel)
			{
				operation.IsCancellationRequested = true;
				cancellation = operation.Cancellation;
			}
		}

		if (cancellation is not null)
		{
			try
			{
				cancellation.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			catch (AggregateException)
			{
			}
		}

		responseSource.TrySetResult(response);
		OnChanged();

		return true;
	}

	public bool RequestPause(Guid id)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not (TrackedStorageOperationState.Running or TrackedStorageOperationState.Resuming) || !operation.CanPause
				|| operation.IsCancellationRequested)
			{
				return false;
			}

			operation.RequestPause();
		}

		OnChanged();

		return true;
	}

	public bool RequestResume(Guid id)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not (TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused) || !operation.CanPause
				|| operation.IsCancellationRequested)
			{
				return false;
			}

			operation.RequestResume();
		}

		OnChanged();

		return true;
	}

	public bool Remove(Guid id)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || IsActiveState(operation.State))
			{
				return false;
			}

			_operations.Remove(id);
			_operationOrder.Remove(id);
		}

		OnChanged();

		return true;
	}

	public void ClearCompleted()
	{
		var changed = false;
		lock (_syncRoot)
		{
			for (var index = _operationOrder.Count - 1; index >= 0; index--)
			{
				var id = _operationOrder[index];
				if (IsActiveState(_operations[id].State))
				{
					continue;
				}

				_operations.Remove(id);
				_operationOrder.RemoveAt(index);
				changed = true;
			}
		}

		if (changed)
		{
			OnChanged();
		}
	}

	public void Dispose()
	{
		CancellationTokenSource[] cancellations;
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
			cancellations = [.. _operations.Values.Select(static operation => operation.Cancellation).OfType<CancellationTokenSource>()];
			_operations.Clear();
			_operationOrder.Clear();
			Changed = null;
		}

		foreach (var cancellation in cancellations)
		{
			try
			{
				cancellation.Cancel();
			}
			catch (AggregateException)
			{
			}

			cancellation.Dispose();
		}
	}

	internal void ReportProgress(Guid id, int completedItems, string? currentItemName, long? completedBytes, long? totalBytes, bool isByteProgressForWholeOperation)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(completedItems);

		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				return;
			}

			if (operation.IsCancellationAcknowledged || operation.State is TrackedStorageOperationState.Paused or TrackedStorageOperationState.Resuming or TrackedStorageOperationState.WaitingForUser)
			{
				return;
			}

			if (completedItems > operation.TotalItems)
			{
				throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed items cannot exceed the total item count.");
			}

			operation.CompletedItems = completedItems;
			operation.CurrentItemName = currentItemName;
			operation.IsByteProgressForWholeOperation = isByteProgressForWholeOperation;
			operation.UpdateTransferProgress(completedBytes, totalBytes, isByteProgressForWholeOperation);
		}

		OnChanged();
	}

	internal void Complete(Guid id)
	{
		TransitionToTerminalState(id, TrackedStorageOperationState.Succeeded, null);
	}

	internal void Fail(Guid id, Exception error)
	{
		ArgumentNullException.ThrowIfNull(error);

		TransitionToTerminalState(id, TrackedStorageOperationState.Failed, error);
	}

	internal void MarkCanceled(Guid id)
	{
		TransitionToTerminalState(id, TrackedStorageOperationState.Canceled, null);
	}

	internal bool IsPauseRequested(Guid id)
	{
		lock (_syncRoot)
		{
			return _operations.TryGetValue(id, out var operation) && operation.IsPauseRequested;
		}
	}

	internal void AcknowledgePauseState(Guid id, bool isPaused)
	{
		bool changed;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				return;
			}

			changed = operation.AcknowledgePause(isPaused);
		}

		if (changed)
		{
			OnChanged();
		}
	}

	internal void AcknowledgeCancellationRequest(Guid id)
	{
		bool changed;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				return;
			}

			changed = operation.AcknowledgeCancellation();
		}

		if (changed)
		{
			OnChanged();
		}
	}

	internal ValueTask<StorageOperationInterruptionResponse> RequestInterruptionAsync(Guid id, StorageOperationInterruption interruption, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(interruption);
		cancellationToken.ThrowIfCancellationRequested();

		Task<StorageOperationInterruptionResponse> responseTask;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				throw new InvalidOperationException("The storage operation cannot accept an interruption in its current state.");
			}

			if (operation.TryGetRememberedInterruptionResponse(interruption, out var rememberedResponse))
			{
				return ValueTask.FromResult(rememberedResponse);
			}

			responseTask = operation.BeginInterruption(interruption);
		}

		OnChanged();

		return new ValueTask<StorageOperationInterruptionResponse>(responseTask.WaitAsync(cancellationToken));
	}

	private static bool IsActiveState(TrackedStorageOperationState state)
	{
		return state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming or TrackedStorageOperationState.WaitingForUser;
	}

	internal static bool IsDecisionAvailable(StorageOperationInterruption interruption, StorageOperationInterruptionDecision decision)
	{
		var option = decision switch
		{
			StorageOperationInterruptionDecision.Retry => StorageOperationInterruptionResponses.Retry,
			StorageOperationInterruptionDecision.Skip => StorageOperationInterruptionResponses.Skip,
			StorageOperationInterruptionDecision.Cancel => StorageOperationInterruptionResponses.Cancel,
			StorageOperationInterruptionDecision.Continue => StorageOperationInterruptionResponses.Continue,
			StorageOperationInterruptionDecision.Yes => StorageOperationInterruptionResponses.Yes,
			StorageOperationInterruptionDecision.No => StorageOperationInterruptionResponses.No,
			StorageOperationInterruptionDecision.Delete => StorageOperationInterruptionResponses.Delete,
			StorageOperationInterruptionDecision.Ok => StorageOperationInterruptionResponses.Ok,
			_ => StorageOperationInterruptionResponses.None,
		};

		return interruption.AvailableResponses.HasFlag(option);
	}

	private void TransitionToTerminalState(Guid id, TrackedStorageOperationState state, Exception? error)
	{
		CancellationTokenSource? cancellation;
		TaskCompletionSource<StorageOperationInterruptionResponse>? responseSource;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				return;
			}

			operation.State = state;
			operation.Error = error;
			operation.CompletedAt = DateTimeOffset.Now;
			if (state is TrackedStorageOperationState.Succeeded)
			{
				operation.CompletedItems = operation.TotalItems;
			}

			operation.UpdateTransferProgress(null, null, isByteProgressForWholeOperation: false);

			cancellation = operation.Cancellation;
			operation.Cancellation = null;
			responseSource = operation.InterruptionResponseSource;
			operation.ClearInterruption();
		}

		responseSource?.TrySetResult(new StorageOperationInterruptionResponse(StorageOperationInterruptionDecision.Cancel));
		cancellation?.Dispose();
		OnChanged();
	}

	private void OnChanged()
	{
		var handlers = Changed;
		if (handlers is null)
		{
			return;
		}

		foreach (EventHandler handler in handlers.GetInvocationList())
		{
			try
			{
				handler(this, EventArgs.Empty);
			}
			catch (Exception error)
			{
				_logger.LogDebug("Status observer failed: {ExceptionType}", error.GetType().Name);
			}
		}
	}


}

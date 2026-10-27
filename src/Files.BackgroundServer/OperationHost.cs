// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using Files.Core.Storage;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Files.BackgroundServer;

internal sealed class OperationHost(ServerLifetime lifetime)
{
	private const int MaximumRetainedOperations = 256;
	private readonly object _syncRoot = new();
	private readonly Guid _serverInstanceId = Guid.NewGuid();
	private readonly Dictionary<Guid, OperationEntry> _operations = [];
	private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private ulong _revision;

	internal Guid Submit(IServerOperation operation)
	{
		ArgumentNullException.ThrowIfNull(operation);

		var id = operation.Id;
		var kind = operation.Kind;
		if (id == Guid.Empty || string.IsNullOrWhiteSpace(kind) || kind.Length > 128)
		{
			throw new ArgumentException("An operation must have a nonempty identity and a bounded kind.", nameof(operation));
		}

		var activity = lifetime.Acquire();
		try
		{
			lock (_syncRoot)
			{
				if (_operations.ContainsKey(id))
				{
					throw new InvalidOperationException("The operation identity is already retained. Reconcile its status before submitting again.");
				}

				if (_operations.Count >= MaximumRetainedOperations)
				{
					throw new InvalidOperationException("The operation history is full. Forget completed operations before submitting more work.");
				}

				var entry = new OperationEntry(operation);
				_operations.Add(id, entry);
				PublishChange();
				_ = Task.Run(() => ExecuteAsync(operation, entry, activity));
			}
		}
		catch
		{
			activity.Dispose();

			throw;
		}

		return id;
	}

	internal OperationList GetOperations()
	{
		lock (_syncRoot)
		{
			return new OperationList(_serverInstanceId, _revision, [.. _operations.Values.Select(static entry => entry.Summary)]);
		}
	}

	internal async Task WaitForChangesAsync(Guid serverInstanceId, ulong knownRevision, CancellationToken cancellationToken)
	{
		// Bound observation frequency without posting one UI callback for every provider update.
		await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
		Task changed;
		lock (_syncRoot)
		{
			if (serverInstanceId != _serverInstanceId || knownRevision != _revision)
			{
				return;
			}

			changed = _changed.Task;
		}

		try
		{
			await changed.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			// A bounded wait lets clients refresh their connection even when there are no changes.
		}
	}

	internal bool RequestCancellation(Guid operationId)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(operationId, out var entry) || IsTerminal(entry.Summary.State))
			{
				return false;
			}

			if (!entry.Summary.IsCancellationRequested)
			{
				entry.Summary.IsCancellationRequested = true;
				entry.CancellationTask = entry.Cancellation.CancelAsync();
				PublishChange();
			}

			return true;
		}
	}

	internal bool Forget(Guid operationId)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(operationId, out var entry) || !IsTerminal(entry.Summary.State))
			{
				return false;
			}

			_operations.Remove(operationId);
			PublishChange();

			return true;
		}
	}

	internal bool SetPaused(Guid id, bool paused)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var entry) || IsTerminal(entry.Summary.State) || !entry.Summary.CanPause
				|| entry.Summary.IsCancellationRequested || entry.Summary.HasInterruption)
			{
				return false;
			}

			if (entry.Summary.IsPauseRequested != paused)
			{
				entry.Summary.IsPauseRequested = paused;
				entry.Summary.State = paused ? OperationState.Pausing : OperationState.Resuming;
				PublishChange();
			}

			return true;
		}
	}

	internal bool ResolveInterruption(Guid id, Guid interruptionId, int decision, bool applyToAll)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var entry) || !entry.Summary.HasInterruption || entry.Summary.Interruption.Id != interruptionId || entry.Decision is null)
			{
				return false;
			}

			var selected = (StorageOperationInterruptionDecision)decision;
			if (!IsDecisionAvailable(entry.Summary.Interruption.AvailableResponses, selected))
			{
				return false;
			}

			var response = new StorageOperationInterruptionResponse(selected, applyToAll && entry.Summary.Interruption.CanApplyToAll);
			if (response.ApplyToAll)
			{
				entry.RememberedResponses[(StorageOperationInterruptionKind)entry.Summary.Interruption.Kind] = response;
			}

			var source = entry.Decision;
			entry.Decision = null;
			entry.Summary.HasInterruption = false;
			entry.Summary.State = OperationState.Running;
			if (selected == StorageOperationInterruptionDecision.Cancel)
			{
				entry.Summary.IsCancellationRequested = true;
				entry.CancellationTask = entry.Cancellation.CancelAsync();
			}

			source.TrySetResult(response);
			PublishChange();

			return true;
		}
	}

	internal bool IsPauseRequested(OperationEntry entry)
	{
		lock (_syncRoot)
		{
			return entry.Summary.IsPauseRequested && !entry.Summary.IsCancellationRequested;
		}
	}

	internal void AcknowledgePause(OperationEntry entry, bool paused)
	{
		lock (_syncRoot)
		{
			if (IsTerminal(entry.Summary.State) || entry.Summary.HasInterruption)
			{
				return;
			}

			entry.Summary.State = (paused, entry.Summary.IsPauseRequested && !entry.Summary.IsCancellationRequested) switch
			{
				(true, true) => OperationState.Paused,
				(true, false) => OperationState.Resuming,
				(false, true) => OperationState.Pausing,
				_ => OperationState.Running,
			};
			PublishChange();
		}
	}

	internal void AcknowledgeCancellation(OperationEntry entry)
	{
		lock (_syncRoot)
		{
			entry.Summary.IsCancellationAcknowledged = true;
			entry.Summary.IsPauseRequested = false;
			PublishChange();
		}
	}

	internal ValueTask<StorageOperationInterruptionResponse> RequestInterruptionAsync(OperationEntry entry, StorageOperationInterruption interruption, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		Task<StorageOperationInterruptionResponse> response;
		lock (_syncRoot)
		{
			if (entry.RememberedResponses.TryGetValue(interruption.Kind, out var remembered) && IsDecisionAvailable((uint)interruption.AvailableResponses, remembered.Decision))
			{
				return ValueTask.FromResult(remembered);
			}

			if (entry.Decision is not null)
			{
				throw new InvalidOperationException("The operation already has a pending decision.");
			}

			entry.Decision = new TaskCompletionSource<StorageOperationInterruptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
			entry.Summary.Interruption = new OperationInterruptionData { Id = Guid.NewGuid(), Kind = (int)interruption.Kind, AvailableResponses = (uint)interruption.AvailableResponses,
				ErrorCode = interruption.ErrorCode, ItemName = interruption.ItemName ?? string.Empty, DestinationPath = interruption.DestinationPath ?? string.Empty,
				CanApplyToAll = interruption.CanApplyToAll };
			entry.Summary.HasInterruption = true;
			entry.Summary.IsPauseRequested = false;
			entry.Summary.State = OperationState.WaitingForUser;
			response = entry.Decision.Task;
			PublishChange();
		}

		ScheduleUnattendedCancellation();

		return new ValueTask<StorageOperationInterruptionResponse>(response.WaitAsync(cancellationToken));
	}

	internal void SetResult(OperationEntry entry, StorageOperationResult result)
	{
		lock (_syncRoot)
		{
			entry.Summary.HasResultItem = result.ResultItem is not null;
			entry.Summary.ResultItem = result.ResultItem is null ? default : StorageOperationExecutor.ToData(result.ResultItem);
			entry.Summary.Skipped = result.Skipped;
			if (result.Transfer is { } transfer)
			{
				entry.Summary.HasTransferOutcome = true;
				entry.Summary.TransferPhase = (int)transfer.Phase;
				entry.Summary.TransferCompletedBytes += transfer.CompletedBytes;
				entry.Summary.TransferCommittedItems += transfer.CommittedItems;
				if (transfer.Destination is { } destination)
				{
					entry.Summary.TransferDestination = StorageOperationExecutor.ToData(destination);
				}

				entry.Summary.TransferPublication = (int)(transfer.Publication == StorageTransferPublication.Unknown || entry.Summary.TransferPublication == (int)StorageTransferPublication.Unknown
					? StorageTransferPublication.Unknown
					: entry.Summary.TransferCommittedItems > 0 ? result.Succeeded ? StorageTransferPublication.Committed : StorageTransferPublication.Partial : StorageTransferPublication.None);
			}
		}
	}

	internal void ScheduleUnattendedCancellation()
	{
		if (!lifetime.HasClients)
		{
			_ = CancelUnattendedAsync(lifetime.ClientGeneration);
		}
	}

	internal void Report(OperationEntry entry, OperationProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);

		ValidateCounts(progress.CompletedItems, progress.TotalItems);

		ValidateCounts(progress.CompletedBytes, progress.TotalBytes);

		lock (_syncRoot)
		{
			if (IsTerminal(entry.Summary.State))
			{
				return;
			}

			entry.Summary.HasItemProgress = progress.TotalItems.HasValue;
			entry.Summary.CompletedItems = progress.CompletedItems ?? 0;
			entry.Summary.TotalItems = progress.TotalItems ?? 0;
			entry.Summary.HasByteProgress = progress.TotalBytes.HasValue;
			entry.Summary.CompletedBytes = progress.CompletedBytes ?? 0;
			entry.Summary.TotalBytes = progress.TotalBytes ?? 0;
			entry.Summary.CurrentItemName = progress.CurrentItemName ?? string.Empty;
			entry.Summary.IsByteProgressForWholeOperation = progress.IsByteProgressForWholeOperation;
			PublishChange();
		}
	}

	private async Task ExecuteAsync(IServerOperation operation, OperationEntry entry, IDisposable activity)
	{
		try
		{
			lock (_syncRoot)
			{
				entry.Summary.State = OperationState.Running;
				PublishChange();
			}

			entry.Cancellation.Token.ThrowIfCancellationRequested();
			// Implementations retain their provider's scheduler, including the Shell operation STA.
			await operation.ExecuteAsync(new OperationExecutionContext(this, entry), entry.Cancellation.Token).ConfigureAwait(false);
			Complete(entry, OperationState.Succeeded);
		}
		catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
		{
			Complete(entry, OperationState.Canceled);
		}
		catch (Exception exception)
		{
			Complete(entry, OperationState.Failed, exception);
		}
		finally
		{
			try
			{
				await entry.CancellationTask.ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Operation cancellation callback failed: {exception}");
			}
			finally
			{
				entry.Cancellation.Dispose();
				activity.Dispose();
			}
		}
	}

	private async Task CancelUnattendedAsync(long generation)
	{
		await Task.Delay(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
		lock (_syncRoot)
		{
			if (lifetime.HasClients || lifetime.ClientGeneration != generation)
			{
				return;
			}

			foreach (var entry in _operations.Values)
			{
				if (entry.Summary.State is OperationState.Paused or OperationState.Pausing or OperationState.WaitingForUser)
				{
					RequestCancellation(entry.Summary.Id);
				}
			}
		}
	}

	private static bool IsDecisionAvailable(uint responses, StorageOperationInterruptionDecision decision)
	{
		return Enum.IsDefined(decision) && (responses & (1u << (int)decision)) != 0;
	}

	private void Complete(OperationEntry entry, OperationState state, Exception? error = null)
	{
		lock (_syncRoot)
		{
			entry.Summary.State = state;
			if (state is OperationState.Failed or OperationState.Canceled && entry.Summary.HasTransferOutcome && entry.Summary.TransferCommittedItems > 0
				&& entry.Summary.TransferPublication != (int)StorageTransferPublication.Unknown)
			{
				entry.Summary.TransferPublication = (int)StorageTransferPublication.Partial;
			}

			entry.Summary.IsPauseRequested = false;
			entry.Summary.HasInterruption = false;
			entry.Decision?.TrySetCanceled();
			entry.Decision = null;
			entry.Summary.HasCompletedAt = true;
			entry.Summary.CompletedAt = DateTimeOffset.UtcNow;
			entry.Summary.ErrorCode = error?.HResult ?? 0;
			entry.Summary.ErrorMessage = error is null ? string.Empty : error.Message[..Math.Min(error.Message.Length, 1024)];
			PublishChange();
		}
	}

	private void PublishChange()
	{
		_revision++;
		var previous = _changed;
		_changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		previous.TrySetResult();
	}

	private static bool IsTerminal(OperationState state)
	{
		return state is OperationState.Succeeded or OperationState.Failed or OperationState.Canceled;
	}

	private static void ValidateCounts(long? completed, long? total)
	{
		if (completed.HasValue != total.HasValue || completed < 0 || total < 0 || completed > total)
		{
			throw new ArgumentException("Completed and total counts must both be absent or satisfy 0 <= completed <= total.");
		}
	}

}

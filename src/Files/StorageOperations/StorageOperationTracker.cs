// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using System.Diagnostics;
using System.IO;

namespace Files.StorageOperations;

internal enum TrackedStorageOperationKind
{
	Copy,
	Move,
	Delete,
}

internal enum TrackedStorageOperationState
{
	Running,
	Succeeded,
	Failed,
	Canceled,
}

internal sealed record StorageOperationSnapshot(
	Guid Id,
	TrackedStorageOperationKind Kind,
	TrackedStorageOperationState State,
	int CompletedItems,
	int TotalItems,
	string? CurrentItemName,
	long? CompletedBytes,
	long? TotalBytes,
	double? BytesPerSecond,
	TimeSpan? RemainingTime,
	Exception? Error,
	bool CanCancel,
	bool IsCancellationRequested,
	DateTimeOffset StartedAt,
	DateTimeOffset? CompletedAt);

internal sealed class StorageOperationTracker : IDisposable
{
	private readonly Lock _syncRoot = new();
	private readonly Dictionary<Guid, OperationState> _operations = [];
	private readonly List<Guid> _operationOrder = [];
	private bool _isDisposed;

	public event EventHandler? Changed;

	public StorageOperationHandle StartOperation(TrackedStorageOperationKind kind, int totalItems, string? currentItemName, bool canCancel, CancellationToken cancellationToken = default)
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

				throw new ObjectDisposedException(nameof(StorageOperationTracker));
			}

			_operations.Add(id, new OperationState(id, kind, totalItems, currentItemName, canCancel, operationCancellation));
			_operationOrder.Insert(0, id);
		}

		OnChanged();

		return new StorageOperationHandle(this, id, operationCancellation.Token);
	}

	public IReadOnlyList<StorageOperationSnapshot> GetSnapshot()
	{
		lock (_syncRoot)
		{
			return _operationOrder.Select(id => _operations[id].CreateSnapshot()).ToArray();
		}
	}

	public bool RequestCancellation(Guid id)
	{
		CancellationTokenSource? cancellation;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not TrackedStorageOperationState.Running || !operation.CanCancel || operation.IsCancellationRequested)
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

	public bool Remove(Guid id)
	{
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is TrackedStorageOperationState.Running)
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
				if (_operations[id].State is TrackedStorageOperationState.Running)
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
			cancellations = _operations.Values.Select(static operation => operation.Cancellation).OfType<CancellationTokenSource>().ToArray();
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

	internal void ReportProgress(Guid id, int completedItems, string? currentItemName, long? completedBytes, long? totalBytes)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(completedItems);

		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not TrackedStorageOperationState.Running)
			{
				return;
			}

			if (completedItems > operation.TotalItems)
			{
				throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed items cannot exceed the total item count.");
			}

			operation.CompletedItems = completedItems;
			operation.CurrentItemName = currentItemName;
			operation.UpdateTransferProgress(completedBytes, totalBytes);
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

	private void TransitionToTerminalState(Guid id, TrackedStorageOperationState state, Exception? error)
	{
		CancellationTokenSource? cancellation;
		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || operation.State is not TrackedStorageOperationState.Running)
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

			operation.UpdateTransferProgress(null, null);

			cancellation = operation.Cancellation;
			operation.Cancellation = null;
		}

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
				UiDiagnosticLog.Write("StorageOperationTracker", $"Status observer failed: {error.GetType().Name}");
			}
		}
	}

	private sealed class OperationState
	{
		private long? _previousCompletedBytes;
		private long _previousProgressTimestamp;

		public Guid Id { get; }
		public TrackedStorageOperationKind Kind { get; }
		public int TotalItems { get; }
		public bool CanCancel { get; }
		public DateTimeOffset StartedAt { get; }
		public TrackedStorageOperationState State { get; set; }
		public int CompletedItems { get; set; }
		public string? CurrentItemName { get; set; }
		public long? CompletedBytes { get; private set; }
		public long? TotalBytes { get; private set; }
		public double? BytesPerSecond { get; private set; }
		public TimeSpan? RemainingTime { get; private set; }
		public Exception? Error { get; set; }
		public bool IsCancellationRequested { get; set; }
		public DateTimeOffset? CompletedAt { get; set; }
		public CancellationTokenSource? Cancellation { get; set; }

		public OperationState(Guid id, TrackedStorageOperationKind kind, int totalItems, string? currentItemName, bool canCancel, CancellationTokenSource cancellation)
		{
			Id = id;
			Kind = kind;
			TotalItems = totalItems;
			CurrentItemName = currentItemName;
			CanCancel = canCancel;
			StartedAt = DateTimeOffset.Now;
			State = TrackedStorageOperationState.Running;
			Cancellation = cancellation;
			_previousProgressTimestamp = Stopwatch.GetTimestamp();
		}

		public StorageOperationSnapshot CreateSnapshot()
		{
			return new StorageOperationSnapshot(Id, Kind, State, CompletedItems, TotalItems, CurrentItemName, CompletedBytes, TotalBytes, BytesPerSecond, RemainingTime, Error, CanCancel,
				IsCancellationRequested, StartedAt, CompletedAt);
		}

		public void UpdateTransferProgress(long? completedBytes, long? totalBytes)
		{
			if (completedBytes is null || totalBytes is null)
			{
				CompletedBytes = null;
				TotalBytes = null;
				BytesPerSecond = null;
				RemainingTime = null;
				_previousCompletedBytes = null;
				_previousProgressTimestamp = Stopwatch.GetTimestamp();

				return;
			}

			if (_previousCompletedBytes is { } previousBytes && completedBytes < previousBytes)
			{
				BytesPerSecond = null;
				_previousCompletedBytes = null;
				_previousProgressTimestamp = Stopwatch.GetTimestamp();
			}

			CompletedBytes = completedBytes;
			TotalBytes = totalBytes;
			var now = Stopwatch.GetTimestamp();
			if (_previousCompletedBytes is { } previousCompletedBytes && completedBytes > previousCompletedBytes)
			{
				var elapsed = Stopwatch.GetElapsedTime(_previousProgressTimestamp, now).TotalSeconds;
				if (elapsed > 0)
				{
					var currentSpeed = (completedBytes.Value - previousCompletedBytes) / elapsed;
					BytesPerSecond = BytesPerSecond is { } previousSpeed ? previousSpeed * 0.75 + currentSpeed * 0.25 : currentSpeed;
				}
			}

			_previousCompletedBytes = completedBytes;
			_previousProgressTimestamp = now;
			var remainingSeconds = BytesPerSecond is > 0 ? Math.Clamp((totalBytes.Value - completedBytes.Value) / BytesPerSecond.Value, 0, TimeSpan.MaxValue.TotalSeconds) : (double?)null;
			RemainingTime = remainingSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
		}
	}
}

internal sealed class StorageOperationHandle
{
	private readonly StorageOperationTracker _tracker;
	private readonly Guid _id;

	public CancellationToken CancellationToken { get; }

	internal StorageOperationHandle(StorageOperationTracker tracker, Guid id, CancellationToken cancellationToken)
	{
		_tracker = tracker;
		_id = id;
		CancellationToken = cancellationToken;
	}

	public void Report(int completedItems, string? currentItemName, long? completedBytes = null, long? totalBytes = null)
	{
		_tracker.ReportProgress(_id, completedItems, currentItemName, completedBytes, totalBytes);
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

internal sealed class StorageOperationBatchProgress : IProgress<StorageOperationProgress>
{
	private readonly StorageOperationHandle _operation;
	private readonly int _completedItems;
	private readonly string _fallbackItemName;

	public StorageOperationBatchProgress(StorageOperationHandle operation, int completedItems, string fallbackItemName)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ArgumentException.ThrowIfNullOrWhiteSpace(fallbackItemName);

		_operation = operation;
		_completedItems = completedItems;
		_fallbackItemName = fallbackItemName;
	}

	public void Report(StorageOperationProgress value)
	{
		ArgumentNullException.ThrowIfNull(value);

		var itemCompleted = value.CompletedItems == value.TotalItems ? 1 : 0;
		var completedBytes = itemCompleted is 0 ? value.CompletedBytes : null;
		var totalBytes = itemCompleted is 0 ? value.TotalBytes : null;
		_operation.Report(_completedItems + itemCompleted, GetItemName(value.CurrentItem), completedBytes, totalBytes);
	}

	private string GetItemName(StorableReference? reference)
	{
		if (reference?.LastKnownAddress is not { } address || string.IsNullOrWhiteSpace(address.Value))
		{
			return _fallbackItemName;
		}

		var trimmedAddress = address.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/');
		var itemName = Path.GetFileName(trimmedAddress);

		return string.IsNullOrWhiteSpace(itemName) ? _fallbackItemName : itemName;
	}
}

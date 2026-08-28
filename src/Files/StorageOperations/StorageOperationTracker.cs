// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Numerics;

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
	Pausing,
	Paused,
	Resuming,
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
	string? DestinationPath,
	long? CompletedBytes,
	long? TotalBytes,
	bool IsByteProgressForWholeOperation,
	double? BytesPerSecond,
	TimeSpan? RemainingTime,
	Exception? Error,
	bool CanCancel,
	bool CanPause,
	bool IsCancellationRequested,
	bool IsCancellationAcknowledged,
	DateTimeOffset StartedAt,
	DateTimeOffset? CompletedAt,
	IReadOnlyList<Vector2> SpeedGraphPoints);

internal sealed class StorageOperationTracker : IDisposable
{
	private const int MaxSpeedGraphPoints = 201;
	private const int SpeedSmoothingSampleCount = 37;
	private const double SpeedGraphPointIntervalPercentage = 0.5;

	private readonly Lock _syncRoot = new();
	private readonly Dictionary<Guid, OperationState> _operations = [];
	private readonly List<Guid> _operationOrder = [];
	private bool _isDisposed;

	public event EventHandler? Changed;

	public StorageOperationHandle StartOperation(TrackedStorageOperationKind kind, int totalItems, string? currentItemName, bool canCancel, CancellationToken cancellationToken = default,
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

				throw new ObjectDisposedException(nameof(StorageOperationTracker));
			}

			_operations.Add(id, new OperationState(id, kind, totalItems, currentItemName, destinationPath, canCancel, canPause, operationCancellation));
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

	internal void ReportProgress(Guid id, int completedItems, string? currentItemName, long? completedBytes, long? totalBytes, bool isByteProgressForWholeOperation)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(completedItems);

		lock (_syncRoot)
		{
			if (!_operations.TryGetValue(id, out var operation) || !IsActiveState(operation.State))
			{
				return;
			}

			if (operation.IsCancellationAcknowledged || operation.State is TrackedStorageOperationState.Paused or TrackedStorageOperationState.Resuming)
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

	private static bool IsActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming;

	private void TransitionToTerminalState(Guid id, TrackedStorageOperationState state, Exception? error)
	{
		CancellationTokenSource? cancellation;
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
		private readonly Queue<double> _itemSpeedSamples = new(SpeedSmoothingSampleCount);
		private readonly List<Vector2> _speedGraphPoints = [];
		private readonly Queue<double> _speedSamples = new(SpeedSmoothingSampleCount);
		private bool _hasByteProgress;
		private double _itemSpeedSampleTotal;
		private double _speedSampleTotal;
		private double _lastSpeedGraphPointProgressPercentage;
		private long? _previousCompletedBytes;
		private int? _previousCompletedItems;
		private long _previousItemProgressTimestamp;
		private long _previousProgressTimestamp;

		public Guid Id { get; }
		public TrackedStorageOperationKind Kind { get; }
		public int TotalItems { get; }
		public bool CanCancel { get; }
		public bool CanPause { get; }
		public DateTimeOffset StartedAt { get; }
		public TrackedStorageOperationState State { get; set; }
		public int CompletedItems { get; set; }
		public string? CurrentItemName { get; set; }
		public string? DestinationPath { get; }
		public long? CompletedBytes { get; private set; }
		public long? TotalBytes { get; private set; }
		public bool IsByteProgressForWholeOperation { get; set; }
		public double? BytesPerSecond { get; private set; }
		public TimeSpan? RemainingTime { get; private set; }
		public Exception? Error { get; set; }
		public bool IsPauseRequested { get; private set; }
		public bool IsCancellationRequested { get; set; }
		public bool IsCancellationAcknowledged { get; private set; }
		public DateTimeOffset? CompletedAt { get; set; }
		public CancellationTokenSource? Cancellation { get; set; }

		public OperationState(Guid id, TrackedStorageOperationKind kind, int totalItems, string? currentItemName, string? destinationPath, bool canCancel, bool canPause,
			CancellationTokenSource cancellation)
		{
			Id = id;
			Kind = kind;
			TotalItems = totalItems;
			CurrentItemName = currentItemName;
			DestinationPath = destinationPath;
			CanCancel = canCancel;
			CanPause = canPause;
			StartedAt = DateTimeOffset.Now;
			State = TrackedStorageOperationState.Running;
			Cancellation = cancellation;
			_previousProgressTimestamp = Stopwatch.GetTimestamp();
			_previousItemProgressTimestamp = _previousProgressTimestamp;
		}

		public StorageOperationSnapshot CreateSnapshot()
		{
			return new StorageOperationSnapshot(Id, Kind, State, CompletedItems, TotalItems, CurrentItemName, DestinationPath, CompletedBytes, TotalBytes, IsByteProgressForWholeOperation, BytesPerSecond,
				RemainingTime, Error, CanCancel, CanPause, IsCancellationRequested, IsCancellationAcknowledged, StartedAt, CompletedAt, _speedGraphPoints.ToArray());
		}

		public void RequestPause()
		{
			IsPauseRequested = true;
			State = State is TrackedStorageOperationState.Resuming ? TrackedStorageOperationState.Paused : TrackedStorageOperationState.Pausing;
		}

		public void RequestResume()
		{
			IsPauseRequested = false;
			State = State is TrackedStorageOperationState.Pausing ? TrackedStorageOperationState.Running : TrackedStorageOperationState.Resuming;
		}

		public bool AcknowledgePause(bool isPaused)
		{
			var state = (isPaused, IsPauseRequested, IsCancellationAcknowledged) switch
			{
				(true, true, false) => TrackedStorageOperationState.Paused,
				(true, _, _) => TrackedStorageOperationState.Resuming,
				(false, true, false) => TrackedStorageOperationState.Pausing,
				_ => TrackedStorageOperationState.Running,
			};
			if (State == state)
			{
				return false;
			}

			State = state;
			BytesPerSecond = null;
			RemainingTime = null;
			ResetSpeedSamples();
			ResetItemSpeedSamples();
			_previousCompletedBytes = isPaused ? CompletedBytes : null;
			_previousCompletedItems = isPaused ? CompletedItems : null;
			var now = Stopwatch.GetTimestamp();
			_previousProgressTimestamp = now;
			_previousItemProgressTimestamp = now;

			return true;
		}

		public bool AcknowledgeCancellation()
		{
			if (IsCancellationAcknowledged)
			{
				return false;
			}

			IsCancellationAcknowledged = true;
			IsCancellationRequested = true;
			IsPauseRequested = false;
			if (State is TrackedStorageOperationState.Paused)
			{
				State = TrackedStorageOperationState.Resuming;
			}
			else if (State is TrackedStorageOperationState.Pausing)
			{
				State = TrackedStorageOperationState.Running;
			}

			BytesPerSecond = null;
			RemainingTime = null;
			ResetSpeedSamples();
			ResetItemSpeedSamples();

			return true;
		}

		public void UpdateTransferProgress(long? completedBytes, long? totalBytes, bool isByteProgressForWholeOperation)
		{
			if (State is TrackedStorageOperationState.Paused or TrackedStorageOperationState.Resuming)
			{
				return;
			}

			if (completedBytes is null || totalBytes is null || totalBytes <= 0)
			{
				CompletedBytes = null;
				TotalBytes = null;
				BytesPerSecond = null;
				RemainingTime = null;
				ResetSpeedSamples();
				_previousCompletedBytes = null;
				_previousProgressTimestamp = Stopwatch.GetTimestamp();
				if (!_hasByteProgress)
				{
					UpdateItemProgress();
				}

				return;
			}

			if (!_hasByteProgress)
			{
				_hasByteProgress = true;
				ResetItemSpeedSamples();
			}

			if (_previousCompletedBytes is { } previousBytes && completedBytes < previousBytes)
			{
				BytesPerSecond = null;
				ResetSpeedSamples();
				_previousCompletedBytes = null;
				_previousProgressTimestamp = Stopwatch.GetTimestamp();
			}

			CompletedBytes = completedBytes;
			TotalBytes = totalBytes;
			var now = Stopwatch.GetTimestamp();
			if (_previousCompletedBytes is { } previousCompletedBytes)
			{
				var elapsed = Stopwatch.GetElapsedTime(_previousProgressTimestamp, now).TotalSeconds;
				if (elapsed > 0)
				{
					var currentSpeed = (completedBytes.Value - previousCompletedBytes) / elapsed;
					AddSpeedSample(currentSpeed);
				}
			}

			_previousCompletedBytes = completedBytes;
			_previousProgressTimestamp = now;
			var remainingSeconds = BytesPerSecond is > 0 ? Math.Clamp((totalBytes.Value - completedBytes.Value) / BytesPerSecond.Value, 0, TimeSpan.MaxValue.TotalSeconds) : (double?)null;
			RemainingTime = remainingSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
			if (totalBytes > 0)
			{
				var currentItemProgress = Math.Clamp((double)completedBytes.Value / totalBytes.Value, 0, 1);
				var progressPercentage = isByteProgressForWholeOperation ? currentItemProgress * 100d : Math.Clamp((CompletedItems + currentItemProgress) * 100d / TotalItems, 0, 100);
				AddSpeedGraphPoint(progressPercentage, BytesPerSecond ?? 0);
			}
		}

		private void UpdateItemProgress()
		{
			var now = Stopwatch.GetTimestamp();
			var averageSpeed = 0d;
			if (_previousCompletedItems is { } previousCompletedItems)
			{
				if (CompletedItems < previousCompletedItems)
				{
					ResetItemSpeedSamples();
				}
				else
				{
					var elapsed = Stopwatch.GetElapsedTime(_previousItemProgressTimestamp, now).TotalSeconds;
					if (elapsed > 0)
					{
						averageSpeed = AddItemSpeedSample((CompletedItems - previousCompletedItems) / elapsed);
					}
				}
			}

			_previousCompletedItems = CompletedItems;
			_previousItemProgressTimestamp = now;
			var progressPercentage = Math.Clamp((double)CompletedItems * 100d / TotalItems, 0, 100);
			AddSpeedGraphPoint(progressPercentage, averageSpeed);
		}

		private double AddItemSpeedSample(double speed)
		{
			if (!double.IsFinite(speed) || speed < 0)
			{
				return 0;
			}

			_itemSpeedSamples.Enqueue(speed);
			_itemSpeedSampleTotal += speed;
			if (_itemSpeedSamples.Count > SpeedSmoothingSampleCount)
			{
				_itemSpeedSampleTotal -= _itemSpeedSamples.Dequeue();
			}

			return _itemSpeedSampleTotal / _itemSpeedSamples.Count;
		}

		private void AddSpeedSample(double speed)
		{
			if (!double.IsFinite(speed) || speed < 0)
			{
				return;
			}

			_speedSamples.Enqueue(speed);
			_speedSampleTotal += speed;
			if (_speedSamples.Count > SpeedSmoothingSampleCount)
			{
				_speedSampleTotal -= _speedSamples.Dequeue();
			}

			BytesPerSecond = _speedSampleTotal / _speedSamples.Count;
		}

		private void AddSpeedGraphPoint(double progressPercentage, double rate)
		{
			var point = new Vector2((float)progressPercentage, (float)Math.Clamp(rate, 0, float.MaxValue));
			if (_speedGraphPoints.Count is 0)
			{
				if (progressPercentage > 0)
				{
					_speedGraphPoints.Add(Vector2.Zero);
				}

				_speedGraphPoints.Add(point);
				_lastSpeedGraphPointProgressPercentage = progressPercentage;

				return;
			}

			var lastPoint = _speedGraphPoints[^1];
			if (point.X < lastPoint.X)
			{
				return;
			}

			if (progressPercentage - _lastSpeedGraphPointProgressPercentage < SpeedGraphPointIntervalPercentage)
			{
				_speedGraphPoints[^1] = point;

				return;
			}

			if (_speedGraphPoints.Count >= MaxSpeedGraphPoints)
			{
				_speedGraphPoints.RemoveAt(0);
			}

			_speedGraphPoints.Add(point);
			_lastSpeedGraphPointProgressPercentage = progressPercentage;
		}

		private void ResetSpeedSamples()
		{
			_speedSamples.Clear();
			_speedSampleTotal = 0;
		}

		private void ResetItemSpeedSamples()
		{
			_itemSpeedSamples.Clear();
			_itemSpeedSampleTotal = 0;
			_previousCompletedItems = null;
			_previousItemProgressTimestamp = Stopwatch.GetTimestamp();
		}
	}
}

internal sealed class StorageOperationHandle : IStorageOperationControl
{
	private readonly StorageOperationTracker _tracker;
	private readonly Guid _id;

	public CancellationToken CancellationToken { get; }

	public bool IsPauseRequested => _tracker.IsPauseRequested(_id);

	internal StorageOperationHandle(StorageOperationTracker tracker, Guid id, CancellationToken cancellationToken)
	{
		_tracker = tracker;
		_id = id;
		CancellationToken = cancellationToken;
	}

	public void AcknowledgePauseState(bool isPaused) => _tracker.AcknowledgePauseState(_id, isPaused);

	public void AcknowledgeCancellationRequest() => _tracker.AcknowledgeCancellationRequest(_id);

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

internal sealed class StorageOperationBatchProgress : IProgress<StorageOperationProgress>
{
	private readonly StorageOperationHandle _operation;
	private readonly int _completedItems;
	private readonly string _fallbackItemName;
	private readonly long? _completedBytesBeforeCurrentItem;
	private readonly long? _currentItemBytes;
	private readonly long? _totalBatchBytes;

	public StorageOperationBatchProgress(StorageOperationHandle operation, int completedItems, string fallbackItemName, long? completedBytesBeforeCurrentItem = null, long? currentItemBytes = null,
		long? totalBatchBytes = null)
	{
		ArgumentNullException.ThrowIfNull(operation);

		ArgumentException.ThrowIfNullOrWhiteSpace(fallbackItemName);

		ArgumentOutOfRangeException.ThrowIfNegative(completedBytesBeforeCurrentItem ?? 0);

		ArgumentOutOfRangeException.ThrowIfNegative(currentItemBytes ?? 0);

		ArgumentOutOfRangeException.ThrowIfNegative(totalBatchBytes ?? 0);

		_operation = operation;
		_completedItems = completedItems;
		_fallbackItemName = fallbackItemName;
		_completedBytesBeforeCurrentItem = completedBytesBeforeCurrentItem;
		_currentItemBytes = currentItemBytes;
		_totalBatchBytes = totalBatchBytes;
	}

	public void Report(StorageOperationProgress value)
	{
		ArgumentNullException.ThrowIfNull(value);

		var itemCompleted = value.CompletedItems == value.TotalItems ? 1 : 0;
		if (_completedBytesBeforeCurrentItem is { } completedBytesBeforeCurrentItem && _currentItemBytes is { } currentItemBytes && _totalBatchBytes is { } totalBatchBytes)
		{
			var currentItemCompletedBytes = itemCompleted is 1 ? currentItemBytes : Math.Clamp(value.CompletedBytes ?? 0, 0, currentItemBytes);
			_operation.Report(_completedItems + itemCompleted, GetItemName(value.CurrentItem), completedBytesBeforeCurrentItem + currentItemCompletedBytes, totalBatchBytes,
				isByteProgressForWholeOperation: true);

			return;
		}

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

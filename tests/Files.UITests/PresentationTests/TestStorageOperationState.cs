// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace Files.UITests;

internal sealed class TestStorageOperationState
{
	private const int MaxSpeedGraphPoints = 201;
	private const int SpeedSmoothingSampleCount = 37;
	private const double SpeedGraphPointIntervalPercentage = 0.5;
	private readonly Queue<double> _itemSpeedSamples = new(SpeedSmoothingSampleCount);
	private readonly Dictionary<StorageOperationInterruptionKind, StorageOperationInterruptionResponse> _rememberedInterruptionResponses = [];
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
	public StorageOperationInterruption? Interruption { get; private set; }
	public Guid InterruptionId { get; private set; }
	public TaskCompletionSource<StorageOperationInterruptionResponse>? InterruptionResponseSource { get; private set; }
	public Exception? Error { get; set; }
	public bool IsPauseRequested { get; private set; }
	public bool IsCancellationRequested { get; set; }
	public bool IsCancellationAcknowledged { get; private set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public CancellationTokenSource? Cancellation { get; set; }

	public TestStorageOperationState(Guid id, TrackedStorageOperationKind kind, int totalItems, string? currentItemName, string? destinationPath, bool canCancel, bool canPause,
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

	public StorageOperationStatus CreateStatus()
	{
		return new StorageOperationStatus(Id, Kind, State, CompletedItems, TotalItems, CurrentItemName, DestinationPath, CompletedBytes, TotalBytes, IsByteProgressForWholeOperation, BytesPerSecond,
			RemainingTime, Interruption, InterruptionId, Error, CanCancel, CanPause, IsCancellationRequested, IsCancellationAcknowledged, StartedAt, CompletedAt, _speedGraphPoints.ToArray());
	}

	public Task<StorageOperationInterruptionResponse> BeginInterruption(StorageOperationInterruption interruption)
	{
		if (InterruptionResponseSource is not null)
		{
			throw new InvalidOperationException("The storage operation is already waiting for an interruption response.");
		}

		Interruption = interruption;
		InterruptionId = Guid.NewGuid();
		InterruptionResponseSource = new TaskCompletionSource<StorageOperationInterruptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		State = TrackedStorageOperationState.WaitingForUser;
		IsPauseRequested = false;
		BytesPerSecond = null;
		RemainingTime = null;
		ResetSpeedSamples();
		ResetItemSpeedSamples();

		return InterruptionResponseSource.Task;
	}

	public void ClearInterruption()
	{
		Interruption = null;
		InterruptionResponseSource = null;
	}

	public void RememberInterruptionResponse(StorageOperationInterruptionKind kind, StorageOperationInterruptionResponse response)
	{
		_rememberedInterruptionResponses[kind] = response;
	}

	public void ResumeFromInterruption()
	{
		ClearInterruption();
		State = TrackedStorageOperationState.Running;
		var now = Stopwatch.GetTimestamp();
		_previousProgressTimestamp = now;
		_previousItemProgressTimestamp = now;
		_previousCompletedBytes = CompletedBytes;
		_previousCompletedItems = CompletedItems;
	}

	public bool TryGetRememberedInterruptionResponse(StorageOperationInterruption interruption, out StorageOperationInterruptionResponse response)
	{
		if (_rememberedInterruptionResponses.TryGetValue(interruption.Kind, out response) && TestStorageOperationController.IsDecisionAvailable(interruption, response.Decision))
		{
			return true;
		}

		response = default;

		return false;
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

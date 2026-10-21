// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Numerics;

namespace Files.StorageOperations;

internal sealed class StorageOperationProgressDisplay
{
	private const int MaxSpeedGraphPoints = 201;
	private const int SpeedSmoothingSampleCount = 37;
	private const double SpeedGraphPointIntervalPercentage = 0.5;
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
	private bool _initialized;
	private bool _isByteProgressForWholeOperation;
	private int TotalItems { get; set; }
	private int CompletedItems { get; set; }
	private TrackedStorageOperationState State { get; set; }
	private long? CompletedBytes { get; set; }
	private long? TotalBytes { get; set; }
	private double? BytesPerSecond { get; set; }
	private TimeSpan? RemainingTime { get; set; }

	internal StorageOperationStatus Update(StorageOperationStatus status)
	{
		if (_initialized && State == status.State && TotalItems == status.TotalItems && CompletedItems == status.CompletedItems && CompletedBytes == status.CompletedBytes
			&& TotalBytes == status.TotalBytes && _isByteProgressForWholeOperation == status.IsByteProgressForWholeOperation)
		{
			return WithDisplayProgress(status);
		}

		_initialized = true;
		_isByteProgressForWholeOperation = status.IsByteProgressForWholeOperation;
		if (State != status.State)
		{
			ResetSpeedSamples();
			ResetItemSpeedSamples();
			_previousCompletedBytes = null;
			_previousProgressTimestamp = Stopwatch.GetTimestamp();
		}

		State = status.State;
		TotalItems = status.TotalItems;
		CompletedItems = status.CompletedItems;
		if (State is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing)
		{
			UpdateTransferProgress(status.CompletedBytes, status.TotalBytes, status.IsByteProgressForWholeOperation);
		}
		else
		{
			BytesPerSecond = null;
			RemainingTime = null;
		}

		return WithDisplayProgress(status);
	}

	private StorageOperationStatus WithDisplayProgress(StorageOperationStatus status)
	{
		return status with { BytesPerSecond = BytesPerSecond, RemainingTime = RemainingTime, SpeedGraphPoints = _speedGraphPoints.ToArray() };
	}

	private void UpdateTransferProgress(long? completedBytes, long? totalBytes, bool isByteProgressForWholeOperation)
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

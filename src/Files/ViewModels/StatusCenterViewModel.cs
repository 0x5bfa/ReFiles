// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Infrastructure;
using Files.Localization;
using Files.StorageOperations;

namespace Files.ViewModels;

public sealed class StatusCenterViewModel : ObservableObject, IDisposable
{
	private readonly StorageOperationTracker _tracker;
	private readonly IUIDispatcher _dispatcher;
	private int _isDisposed;
	private int _refreshQueued;

	public ObservableCollection<StatusCenterItemViewModel> Items { get; } = [];

	public bool HasItems => Items.Count is not 0;

	public bool HasCompletedItems => Items.Any(static item => !item.IsRunning);

	public bool HasInProgressItems => InProgressItemCount is not 0;

	public int InProgressItemCount => Items.Count(static item => item.IsRunning);

	public double AverageProgressPercentage => HasInProgressItems ? Items.Where(static item => item.IsRunning).Average(static item => item.ProgressPercentage) : 0;

	internal StatusCenterViewModel(StorageOperationTracker tracker, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(tracker);

		ArgumentNullException.ThrowIfNull(dispatcher);

		_tracker = tracker;
		_dispatcher = dispatcher;
		_tracker.Changed += Tracker_Changed;
		QueueRefresh();
	}

	public void ClearCompleted()
	{
		_tracker.ClearCompleted();
	}

	public void Cancel(Guid operationId)
	{
		_tracker.RequestCancellation(operationId);
	}

	public void Remove(Guid operationId)
	{
		_tracker.Remove(operationId);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_tracker.Changed -= Tracker_Changed;
	}

	private void Tracker_Changed(object? sender, EventArgs e)
	{
		QueueRefresh();
	}

	private void QueueRefresh()
	{
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		if (_dispatcher.HasThreadAccess)
		{
			Refresh();

			return;
		}

		if (Interlocked.Exchange(ref _refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!_dispatcher.TryEnqueue(RefreshQueued))
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
		}
	}

	private void RefreshQueued()
	{
		Interlocked.Exchange(ref _refreshQueued, 0);
		if (Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		Refresh();
	}

	private void Refresh()
	{
		var snapshots = _tracker.GetSnapshot();
		var snapshotIds = snapshots.Select(static snapshot => snapshot.Id).ToHashSet();
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (!snapshotIds.Contains(Items[index].Id))
			{
				Items.RemoveAt(index);
			}
		}

		for (var index = 0; index < snapshots.Count; index++)
		{
			var snapshot = snapshots[index];
			var item = Items.FirstOrDefault(candidate => candidate.Id == snapshot.Id);
			if (item is null)
			{
				Items.Insert(index, new StatusCenterItemViewModel(snapshot));

				continue;
			}

			var currentIndex = Items.IndexOf(item);
			if (currentIndex != index)
			{
				Items.Move(currentIndex, index);
			}

			item.Update(snapshot);
		}

		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasCompletedItems));
		OnPropertyChanged(nameof(HasInProgressItems));
		OnPropertyChanged(nameof(InProgressItemCount));
		OnPropertyChanged(nameof(AverageProgressPercentage));
	}
}

public sealed class StatusCenterItemViewModel : ObservableObject
{
	private string _title = string.Empty;
	private string _detail = string.Empty;
	private string _progressText = string.Empty;
	private string _glyph = string.Empty;
	private double _progressPercentage;
	private bool _isRunning;
	private bool _canCancel;

	public Guid Id { get; }

	public string Title
	{
		get => _title;
		private set => SetProperty(ref _title, value);
	}

	public string Detail
	{
		get => _detail;
		private set => SetProperty(ref _detail, value);
	}

	public string ProgressText
	{
		get => _progressText;
		private set => SetProperty(ref _progressText, value);
	}

	public string Glyph
	{
		get => _glyph;
		private set => SetProperty(ref _glyph, value);
	}

	public double ProgressPercentage
	{
		get => _progressPercentage;
		private set => SetProperty(ref _progressPercentage, value);
	}

	public bool IsRunning
	{
		get => _isRunning;
		private set
		{
			if (SetProperty(ref _isRunning, value))
			{
				OnPropertyChanged(nameof(CanRemove));
			}
		}
	}

	public bool CanCancel
	{
		get => _canCancel;
		private set => SetProperty(ref _canCancel, value);
	}

	public bool CanRemove => !IsRunning;

	/// <summary>Gets the smoothed transfer-rate history for the operation.</summary>
	public ObservableCollection<Vector2> SpeedGraphPoints { get; } = [];

	/// <summary>Gets a value indicating whether transfer-rate history is available.</summary>
	public bool HasSpeedGraphPoints => SpeedGraphPoints.Count is not 0;

	internal StatusCenterItemViewModel(StorageOperationSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		Id = snapshot.Id;
		Update(snapshot);
	}

	internal void Update(StorageOperationSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (snapshot.Id != Id)
		{
			throw new ArgumentException("The snapshot must represent the same storage operation.", nameof(snapshot));
		}

		Title = GetTitle(snapshot.Kind, snapshot.State);
		Detail = GetDetail(snapshot);
		ProgressText = GetProgressText(snapshot);
		Glyph = GetGlyph(snapshot.State);
		ProgressPercentage = GetProgressPercentage(snapshot);
		IsRunning = snapshot.State is TrackedStorageOperationState.Running;
		CanCancel = IsRunning && snapshot.CanCancel && !snapshot.IsCancellationRequested;
		var hadSpeedGraphPoints = HasSpeedGraphPoints;
		UpdateSpeedGraphPoints(snapshot.SpeedGraphPoints);
		if (hadSpeedGraphPoints != HasSpeedGraphPoints)
		{
			OnPropertyChanged(nameof(HasSpeedGraphPoints));
		}
	}

	private void UpdateSpeedGraphPoints(IReadOnlyList<Vector2> points)
	{
		ArgumentNullException.ThrowIfNull(points);

		var sharedCount = Math.Min(SpeedGraphPoints.Count, points.Count);
		for (var index = 0; index < sharedCount; index++)
		{
			if (SpeedGraphPoints[index] != points[index])
			{
				SpeedGraphPoints[index] = points[index];
			}
		}

		while (SpeedGraphPoints.Count > points.Count)
		{
			SpeedGraphPoints.RemoveAt(SpeedGraphPoints.Count - 1);
		}

		for (var index = sharedCount; index < points.Count; index++)
		{
			SpeedGraphPoints.Add(points[index]);
		}
	}

	private static double GetProgressPercentage(StorageOperationSnapshot snapshot)
	{
		if (snapshot.IsByteProgressForWholeOperation && snapshot.CompletedBytes is { } aggregateCompletedBytes && snapshot.TotalBytes is > 0 and { } aggregateTotalBytes)
		{
			return Math.Clamp((double)aggregateCompletedBytes * 100d / aggregateTotalBytes, 0, 100);
		}

		var currentItemProgress = snapshot.CompletedBytes is { } completedBytes && snapshot.TotalBytes is > 0 and { } totalBytes
			? Math.Clamp((double)completedBytes / totalBytes, 0, 1)
			: 0;

		return Math.Clamp((snapshot.CompletedItems + currentItemProgress) * 100d / snapshot.TotalItems, 0, 100);
	}

	private static string GetProgressText(StorageOperationSnapshot snapshot)
	{
		if (snapshot.State is TrackedStorageOperationState.Running && snapshot.CompletedBytes is { } completedBytes && snapshot.TotalBytes is { } totalBytes)
		{
			var completedText = FormatBytes(completedBytes);
			var totalText = FormatBytes(totalBytes);
			if (snapshot.BytesPerSecond is > 0 and { } bytesPerSecond && snapshot.RemainingTime is { } remainingTime)
			{
				return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferWithSpeedFormat.GetLocalized(), completedText, totalText, FormatBytes((long)bytesPerSecond),
					FormatRemainingTime(remainingTime));
			}

			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferFormat.GetLocalized(), completedText, totalText);
		}

		return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), snapshot.CompletedItems, snapshot.TotalItems);
	}

	private static string FormatBytes(long bytes)
	{
		string[] suffixes =
		[
			Strings.ByteSymbol.GetLocalized(),
			Strings.KilobyteSymbol.GetLocalized(),
			Strings.MegabyteSymbol.GetLocalized(),
			Strings.GigabyteSymbol.GetLocalized(),
			Strings.TerabyteSymbol.GetLocalized(),
			Strings.PetabyteSymbol.GetLocalized(),
		];
		var value = (double)bytes;
		var suffixIndex = 0;
		while (value >= 1024 && suffixIndex < suffixes.Length - 1)
		{
			value /= 1024;
			suffixIndex++;
		}

		var valueText = suffixIndex is 0 ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString(value < 10 ? "N2" : value < 100 ? "N1" : "N0", CultureInfo.CurrentCulture);

		return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationSizeFormat.GetLocalized(), valueText, suffixes[suffixIndex]);
	}

	private static string FormatRemainingTime(TimeSpan remainingTime)
	{
		var rounded = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(remainingTime.TotalSeconds)));

		return rounded.TotalDays >= 1
			? rounded.ToString(@"d\.hh\:mm\:ss", CultureInfo.CurrentCulture)
			: rounded.TotalHours >= 1
				? rounded.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture)
				: rounded.ToString(@"m\:ss", CultureInfo.CurrentCulture);
	}

	private static string GetTitle(TrackedStorageOperationKind kind, TrackedStorageOperationState state)
	{
		return (kind, state) switch
		{
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Running) => Strings.CopyingItems.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Running) => Strings.MovingItems.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Running) => Strings.DeletingItems.GetLocalized(),
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Succeeded) => Strings.CopyCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Succeeded) => Strings.MoveCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Succeeded) => Strings.DeleteCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Failed) => Strings.CopyFailed.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Failed) => Strings.MoveFailed.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Failed) => Strings.DeleteFailed.GetLocalized(),
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Canceled) => Strings.CopyCanceled.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Canceled) => Strings.MoveCanceled.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Canceled) => Strings.DeleteCanceled.GetLocalized(),
			_ => throw new ArgumentOutOfRangeException(nameof(state)),
		};
	}

	private static string GetDetail(StorageOperationSnapshot snapshot)
	{
		if (snapshot.State is TrackedStorageOperationState.Failed)
		{
			return snapshot.Error?.Message ?? Strings.StorageOperationFailed.GetLocalized();
		}

		if (snapshot.State is TrackedStorageOperationState.Canceled)
		{
			return Strings.OperationCanceled.GetLocalized();
		}

		if (snapshot.IsCancellationRequested)
		{
			return Strings.CancelingOperation.GetLocalized();
		}

		return snapshot.CurrentItemName ?? string.Empty;
	}

	private static string GetGlyph(TrackedStorageOperationState state)
	{
		return state switch
		{
			TrackedStorageOperationState.Running => "\uE895",
			TrackedStorageOperationState.Succeeded => "\uE73E",
			TrackedStorageOperationState.Failed => "\uEA39",
			TrackedStorageOperationState.Canceled => "\uE711",
			_ => throw new ArgumentOutOfRangeException(nameof(state)),
		};
	}
}

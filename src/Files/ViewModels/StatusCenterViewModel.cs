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

	public void TogglePaused(Guid operationId)
	{
		var item = Items.FirstOrDefault(candidate => candidate.Id == operationId);
		if (item is null)
		{
			return;
		}

		if (item.IsPauseRequested)
		{
			_tracker.RequestResume(operationId);

			return;
		}

		_tracker.RequestPause(operationId);
	}

	public void ToggleExpanded(Guid operationId)
	{
		Items.FirstOrDefault(item => item.Id == operationId)?.ToggleExpanded();
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
		var snapshots = _tracker.GetSnapshot().OrderBy(static snapshot => IsActiveState(snapshot.State) ? 0 : 1).ToArray();
		var snapshotIds = snapshots.Select(static snapshot => snapshot.Id).ToHashSet();
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (!snapshotIds.Contains(Items[index].Id))
			{
				Items.RemoveAt(index);
			}
		}

		for (var index = 0; index < snapshots.Length; index++)
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

	private static bool IsActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming;
}

public sealed class StatusCenterItemViewModel : ObservableObject
{
	private string _title = string.Empty;
	private string _detail = string.Empty;
	private string _progressText = string.Empty;
	private string _currentItemText = string.Empty;
	private string _transferText = string.Empty;
	private string _speedText = string.Empty;
	private string _remainingText = string.Empty;
	private double _progressPercentage;
	private bool _isRunning;
	private bool _canCancel;
	private bool _canPause;
	private bool _isExpanded;
	private TrackedStorageOperationKind _kind;
	private TrackedStorageOperationState _state;

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

	public string CurrentItemText
	{
		get => _currentItemText;
		private set => SetProperty(ref _currentItemText, value);
	}

	public string TransferText
	{
		get => _transferText;
		private set => SetProperty(ref _transferText, value);
	}

	public string SpeedText
	{
		get => _speedText;
		private set => SetProperty(ref _speedText, value);
	}

	public string RemainingText
	{
		get => _remainingText;
		private set => SetProperty(ref _remainingText, value);
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

	public bool CanPause
	{
		get => _canPause;
		private set => SetProperty(ref _canPause, value);
	}

	public bool CanRemove => !IsRunning;

	public bool IsExpanded
	{
		get => _isExpanded;
		private set
		{
			if (SetProperty(ref _isExpanded, value))
			{
				OnPropertyChanged(nameof(ExpandGlyph));
				OnPropertyChanged(nameof(ExpandAutomationName));
				OnPropertyChanged(nameof(ShowExpandedDetails));
			}
		}
	}

	public string ExpandGlyph => IsExpanded ? "\uE70E" : "\uE70D";

	public string ExpandAutomationName => IsExpanded ? Strings.CollapseDetails.GetLocalized() : Strings.ExpandDetails.GetLocalized();

	public string PauseGlyph => IsPauseRequested ? "\uE768" : "\uE769";

	public string PauseAutomationName => IsPauseRequested ? Strings.Resume.GetLocalized() : Strings.Pause.GetLocalized();

	public bool IsCopyOperation => _kind is TrackedStorageOperationKind.Copy;

	public bool IsMoveOperation => _kind is TrackedStorageOperationKind.Move;

	public bool IsDeleteOperation => _kind is TrackedStorageOperationKind.Delete;

	public bool ShowCopyIcon => IsRunning && IsCopyOperation;

	public bool ShowMoveIcon => IsRunning && IsMoveOperation;

	public bool ShowDeleteIcon => IsRunning && IsDeleteOperation;

	public bool IsSucceeded => _state is TrackedStorageOperationState.Succeeded;

	public bool IsFailed => _state is TrackedStorageOperationState.Failed;

	public bool IsCanceled => _state is TrackedStorageOperationState.Canceled;

	public bool IsPauseRequested => _state is TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused;

	public bool IsPausing => _state is TrackedStorageOperationState.Pausing;

	public bool IsPaused => IsTransferPausedState(_state);

	public bool IsResuming => _state is TrackedStorageOperationState.Resuming;

	public bool IsTransferring => IsTransferActiveState(_state);

	public bool CanTogglePause => IsRunning && CanPause;

	public bool CanExpand => IsRunning;

	public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

	public bool HasCurrentItemText => !string.IsNullOrWhiteSpace(CurrentItemText);

	public bool HasRemainingText => !string.IsNullOrWhiteSpace(RemainingText);

	public bool ShowExpandedDetails => IsRunning && IsExpanded;

	public bool ShowRunningCompactProgress => IsTransferring;

	public bool ShowPausedCompactProgress => IsPaused;

	/// <summary>Gets the smoothed transfer-rate history for the operation.</summary>
	public ObservableCollection<Vector2> SpeedGraphPoints { get; } = [];

	/// <summary>Gets a value indicating whether transfer-rate history is available.</summary>
	public bool HasSpeedGraphPoints => SpeedGraphPoints.Count is not 0;

	internal StatusCenterItemViewModel(StorageOperationSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		Id = snapshot.Id;
		_kind = snapshot.Kind;
		_state = snapshot.State;
		Update(snapshot);
		IsExpanded = IsRunning;
	}

	public void ToggleExpanded()
	{
		if (!CanExpand)
		{
			return;
		}

		IsExpanded = !IsExpanded;
	}

	internal void Update(StorageOperationSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (snapshot.Id != Id)
		{
			throw new ArgumentException("The snapshot must represent the same storage operation.", nameof(snapshot));
		}

		var wasRunning = IsRunning;
		_kind = snapshot.Kind;
		_state = snapshot.State;
		Title = GetTitle(snapshot);
		Detail = GetDetail(snapshot);
		ProgressText = GetProgressText(snapshot);
		CurrentItemText = GetCurrentItemText(snapshot);
		TransferText = GetTransferText(snapshot);
		SpeedText = GetSpeedText(snapshot);
		RemainingText = GetRemainingText(snapshot);
		ProgressPercentage = GetProgressPercentage(snapshot);
		IsRunning = IsActiveState(snapshot.State);
		if (wasRunning && !IsRunning)
		{
			IsExpanded = false;
		}

		CanCancel = IsRunning && snapshot.CanCancel && !snapshot.IsCancellationRequested;
		CanPause = IsRunning && snapshot.CanPause && !snapshot.IsCancellationRequested;
		NotifyPresentationStateChanged();
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

	private void NotifyPresentationStateChanged()
	{
		OnPropertyChanged(nameof(IsCopyOperation));
		OnPropertyChanged(nameof(IsMoveOperation));
		OnPropertyChanged(nameof(IsDeleteOperation));
		OnPropertyChanged(nameof(ShowCopyIcon));
		OnPropertyChanged(nameof(ShowMoveIcon));
		OnPropertyChanged(nameof(ShowDeleteIcon));
		OnPropertyChanged(nameof(IsSucceeded));
		OnPropertyChanged(nameof(IsFailed));
		OnPropertyChanged(nameof(IsCanceled));
		OnPropertyChanged(nameof(IsPauseRequested));
		OnPropertyChanged(nameof(IsPausing));
		OnPropertyChanged(nameof(IsPaused));
		OnPropertyChanged(nameof(IsResuming));
		OnPropertyChanged(nameof(IsTransferring));
		OnPropertyChanged(nameof(CanTogglePause));
		OnPropertyChanged(nameof(PauseGlyph));
		OnPropertyChanged(nameof(PauseAutomationName));
		OnPropertyChanged(nameof(CanExpand));
		OnPropertyChanged(nameof(HasDetail));
		OnPropertyChanged(nameof(HasCurrentItemText));
		OnPropertyChanged(nameof(HasRemainingText));
		OnPropertyChanged(nameof(ShowExpandedDetails));
		OnPropertyChanged(nameof(ShowRunningCompactProgress));
		OnPropertyChanged(nameof(ShowPausedCompactProgress));
	}

	private static bool IsActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming;

	private static bool IsTransferActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing;

	private static bool IsTransferPausedState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Paused or TrackedStorageOperationState.Resuming;

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

	private static string GetCurrentItemText(StorageOperationSnapshot snapshot)
	{
		return string.IsNullOrWhiteSpace(snapshot.CurrentItemName)
			? string.Empty
			: string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationCurrentItemFormat.GetLocalized(), snapshot.CurrentItemName);
	}

	private static string GetTransferText(StorageOperationSnapshot snapshot)
	{
		if (snapshot.CompletedBytes is { } completedBytes && snapshot.TotalBytes is { } totalBytes)
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferredFormat.GetLocalized(), FormatBytes(completedBytes), FormatBytes(totalBytes));
		}

		return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), snapshot.CompletedItems, snapshot.TotalItems);
	}

	private static string GetSpeedText(StorageOperationSnapshot snapshot)
	{
		if (IsTransferPausedState(snapshot.State))
		{
			return Strings.StorageOperationSpeedUnavailable.GetLocalized();
		}

		return snapshot.BytesPerSecond is > 0 and { } bytesPerSecond
			? string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationSpeedFormat.GetLocalized(), FormatBytes((long)bytesPerSecond))
			: Strings.StorageOperationSpeedUnavailable.GetLocalized();
	}

	private static string GetRemainingText(StorageOperationSnapshot snapshot)
	{
		if (!IsTransferActiveState(snapshot.State) || snapshot.RemainingTime is not { } remainingTime)
		{
			return string.Empty;
		}

		if (remainingTime <= TimeSpan.Zero)
		{
			return string.Empty;
		}

		if (remainingTime.TotalMinutes < 1)
		{
			return Strings.StorageOperationLessThanMinuteRemaining.GetLocalized();
		}

		if (remainingTime.TotalMinutes < 2)
		{
			return Strings.StorageOperationAboutOneMinuteRemaining.GetLocalized();
		}

		if (remainingTime.TotalMinutes < 60)
		{
			var roundedMinutes = (int)Math.Ceiling(remainingTime.TotalMinutes);

			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationMinutesRemainingFormat.GetLocalized(), roundedMinutes);
		}

		var roundedHours = Math.Max(1, (int)Math.Ceiling(remainingTime.TotalHours));

		return roundedHours is 1
			? Strings.StorageOperationAboutOneHourRemaining.GetLocalized()
			: string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationHoursRemainingFormat.GetLocalized(), roundedHours);
	}

	private static string GetProgressText(StorageOperationSnapshot snapshot)
	{
		if (IsActiveState(snapshot.State) && snapshot.CompletedBytes is { } completedBytes && snapshot.TotalBytes is { } totalBytes)
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

	private static string GetTitle(StorageOperationSnapshot snapshot)
	{
		if (IsActiveState(snapshot.State))
		{
			var currentItemNumber = Math.Min(snapshot.CompletedItems + 1, snapshot.TotalItems);
			var progressPercentage = (int)Math.Round(GetProgressPercentage(snapshot), MidpointRounding.AwayFromZero);
			var format = (snapshot.Kind, IsTransferPausedState(snapshot.State)) switch
			{
				(TrackedStorageOperationKind.Copy, false) => Strings.CopyingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Move, false) => Strings.MovingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Delete, false) => Strings.DeletingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Copy, true) => Strings.PausedCopyingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Move, true) => Strings.PausedMovingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Delete, true) => Strings.PausedDeletingItemProgressFormat.GetLocalized(),
				_ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
			};

			return string.Format(CultureInfo.CurrentCulture, format, currentItemNumber, snapshot.TotalItems, progressPercentage);
		}

		return (snapshot.Kind, snapshot.State) switch
		{
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Succeeded) => Strings.CopyCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Succeeded) => Strings.MoveCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Succeeded) => Strings.DeleteCompleted.GetLocalized(),
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Failed) => Strings.CopyFailed.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Failed) => Strings.MoveFailed.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Failed) => Strings.DeleteFailed.GetLocalized(),
			(TrackedStorageOperationKind.Copy, TrackedStorageOperationState.Canceled) => Strings.CopyCanceled.GetLocalized(),
			(TrackedStorageOperationKind.Move, TrackedStorageOperationState.Canceled) => Strings.MoveCanceled.GetLocalized(),
			(TrackedStorageOperationKind.Delete, TrackedStorageOperationState.Canceled) => Strings.DeleteCanceled.GetLocalized(),
			_ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
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
			return snapshot.IsCancellationAcknowledged ? Strings.CancelingOperation.GetLocalized() : Strings.WaitingForCancellationOperation.GetLocalized();
		}

		if (snapshot.State is TrackedStorageOperationState.Pausing)
		{
			return Strings.PausingOperation.GetLocalized();
		}

		if (snapshot.State is TrackedStorageOperationState.Resuming)
		{
			return Strings.ResumingOperation.GetLocalized();
		}

		if (snapshot.State is TrackedStorageOperationState.Succeeded)
		{
			return GetCompletionDetail(snapshot);
		}

		if (!string.IsNullOrWhiteSpace(snapshot.DestinationPath))
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationDestinationFormat.GetLocalized(), snapshot.DestinationPath);
		}

		return snapshot.CurrentItemName ?? string.Empty;
	}

	private static string GetCompletionDetail(StorageOperationSnapshot snapshot)
	{
		var itemCount = string.Format(CultureInfo.CurrentCulture, snapshot.TotalItems is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized(), snapshot.TotalItems);

		return snapshot.Kind switch
		{
			TrackedStorageOperationKind.Copy when !string.IsNullOrWhiteSpace(snapshot.DestinationPath) => string.Format(CultureInfo.CurrentCulture,
				Strings.CopyCompletedDetailFormat.GetLocalized(), itemCount, snapshot.DestinationPath),
			TrackedStorageOperationKind.Move when !string.IsNullOrWhiteSpace(snapshot.DestinationPath) => string.Format(CultureInfo.CurrentCulture,
				Strings.MoveCompletedDetailFormat.GetLocalized(), itemCount, snapshot.DestinationPath),
			TrackedStorageOperationKind.Delete => string.Format(CultureInfo.CurrentCulture, Strings.DeleteCompletedDetailFormat.GetLocalized(), itemCount),
			_ => string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), snapshot.CompletedItems, snapshot.TotalItems),
		};
	}
}

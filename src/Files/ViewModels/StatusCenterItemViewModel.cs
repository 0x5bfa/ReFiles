// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Controls;
using Files.Core.Storage;
using Files.Infrastructure;
using Files.Localization;
using Files.StorageOperations;

namespace Files.ViewModels;

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
	private bool _canApplyToAll;
	private TrackedStorageOperationKind _kind;
	private TrackedStorageOperationState _state;
	private StorageOperationInterruption? _interruption;

	public Guid Id { get; }

	internal Guid InterruptionId { get; private set; }

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

	public bool CanApplyToAll
	{
		get => _canApplyToAll;
		private set => SetProperty(ref _canApplyToAll, value);
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

	public bool IsWaitingForUser => _state is TrackedStorageOperationState.WaitingForUser;

	public bool IsTransferring => IsTransferActiveState(_state);

	public bool CanTogglePause => IsRunning && !IsWaitingForUser && CanPause;

	public bool CanExpand => IsRunning && !IsWaitingForUser;

	public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

	public bool HasCurrentItemText => !string.IsNullOrWhiteSpace(CurrentItemText);

	public bool HasRemainingText => !string.IsNullOrWhiteSpace(RemainingText);

	public bool ShowExpandedDetails => IsRunning && IsExpanded;

	public bool ShowRunningCompactProgress => IsTransferring;

	public bool ShowPausedCompactProgress => IsPaused;

	public StorageOperationStatusKind ControlKind => _kind switch
	{
		TrackedStorageOperationKind.Copy => StorageOperationStatusKind.Copy,
		TrackedStorageOperationKind.Move => StorageOperationStatusKind.Move,
		TrackedStorageOperationKind.Delete => StorageOperationStatusKind.Delete,
		_ => StorageOperationStatusKind.Other,
	};

	public StorageOperationStatusState ControlState => _state switch
	{
		TrackedStorageOperationState.Running => StorageOperationStatusState.Running,
		TrackedStorageOperationState.Pausing => StorageOperationStatusState.Pausing,
		TrackedStorageOperationState.Paused => StorageOperationStatusState.Paused,
		TrackedStorageOperationState.Resuming => StorageOperationStatusState.Resuming,
		TrackedStorageOperationState.WaitingForUser => StorageOperationStatusState.WaitingForUser,
		TrackedStorageOperationState.Succeeded => StorageOperationStatusState.Succeeded,
		TrackedStorageOperationState.Failed => StorageOperationStatusState.Failed,
		TrackedStorageOperationState.Canceled => StorageOperationStatusState.Canceled,
		_ => throw new ArgumentOutOfRangeException(nameof(_state)),
	};

	public StorageOperationStatusActions AvailableActions => _interruption?.AvailableResponses switch
	{
		{ } responses => MapAvailableActions(responses),
		null => StorageOperationStatusActions.None,
	};

	public string InterruptionTitle => GetInterruptionTitle(_interruption);

	public string InterruptionMessage => GetInterruptionMessage(_interruption);

	public string InterruptionItemName => _interruption?.ItemName ?? string.Empty;

	public string InterruptionItemDetails => _interruption?.DestinationPath ?? Detail;

	public string PauseButtonText => Strings.Pause.GetLocalized();

	public string ResumeButtonText => Strings.Resume.GetLocalized();

	public string ExpandButtonText => Strings.ExpandDetails.GetLocalized();

	public string CollapseButtonText => Strings.CollapseDetails.GetLocalized();

	public string RemoveButtonText => Strings.Close.GetLocalized();

	public string RetryButtonText => Strings.TryAgain.GetLocalized();

	public string SkipButtonText => Strings.Skip.GetLocalized();

	public string CancelButtonText => Strings.Cancel.GetLocalized();

	public string ContinueButtonText => Strings.Continue.GetLocalized();

	public string YesButtonText => Strings.Yes.GetLocalized();

	public string NoButtonText => Strings.No.GetLocalized();

	public string DeleteButtonText => Strings.Delete.GetLocalized();

	public string OkButtonText => Strings.Ok.GetLocalized();

	public string ApplyToAllText => Strings.DoThisForAllCurrentItems.GetLocalized();

	/// <summary>Gets the smoothed transfer-rate history for the operation.</summary>
	public ObservableCollection<Vector2> SpeedGraphPoints { get; } = [];

	/// <summary>Gets a value indicating whether transfer-rate history is available.</summary>
	public bool HasSpeedGraphPoints => SpeedGraphPoints.Count is not 0;

	internal StatusCenterItemViewModel(StorageOperationStatus operation)
	{
		ArgumentNullException.ThrowIfNull(operation);

		Id = operation.Id;
		_kind = operation.Kind;
		_state = operation.State;
		Update(operation);
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

	internal void Update(StorageOperationStatus operation)
	{
		ArgumentNullException.ThrowIfNull(operation);

		InterruptionId = operation.InterruptionId;

		if (operation.Id != Id)
		{
			throw new ArgumentException("The operation must represent the same storage operation.", nameof(operation));
		}

		var wasRunning = IsRunning;
		_kind = operation.Kind;
		_state = operation.State;
		_interruption = operation.Interruption;
		Title = GetTitle(operation);
		Detail = GetDetail(operation);
		ProgressText = GetProgressText(operation);
		CurrentItemText = GetCurrentItemText(operation);
		TransferText = GetTransferText(operation);
		SpeedText = GetSpeedText(operation);
		RemainingText = GetRemainingText(operation);
		ProgressPercentage = GetProgressPercentage(operation);
		IsRunning = IsActiveState(operation.State);
		if (wasRunning && !IsRunning)
		{
			IsExpanded = false;
		}

		CanCancel = IsRunning && !IsWaitingForUser && operation.CanCancel && !operation.IsCancellationRequested;
		CanPause = IsRunning && !IsWaitingForUser && operation.CanPause && !operation.IsCancellationRequested;
		CanApplyToAll = operation.Interruption?.CanApplyToAll is true && operation.CompletedItems < operation.TotalItems - 1;
		NotifyPresentationStateChanged();
		var hadSpeedGraphPoints = HasSpeedGraphPoints;
		UpdateSpeedGraphPoints(operation.SpeedGraphPoints);
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
		OnPropertyChanged(nameof(IsWaitingForUser));
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
		OnPropertyChanged(nameof(ControlKind));
		OnPropertyChanged(nameof(ControlState));
		OnPropertyChanged(nameof(AvailableActions));
		OnPropertyChanged(nameof(InterruptionTitle));
		OnPropertyChanged(nameof(InterruptionMessage));
		OnPropertyChanged(nameof(InterruptionItemName));
		OnPropertyChanged(nameof(InterruptionItemDetails));
	}

	private static bool IsActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming or TrackedStorageOperationState.WaitingForUser;

	private static bool IsTransferActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing;

	private static bool IsTransferPausedState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Paused or TrackedStorageOperationState.Resuming;

	private static StorageOperationStatusActions MapAvailableActions(StorageOperationInterruptionResponses responses)
	{
		var actions = StorageOperationStatusActions.None;
		if (responses.HasFlag(StorageOperationInterruptionResponses.Retry))
		{
			actions |= StorageOperationStatusActions.Retry;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Skip))
		{
			actions |= StorageOperationStatusActions.Skip;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Cancel))
		{
			actions |= StorageOperationStatusActions.Cancel;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Continue))
		{
			actions |= StorageOperationStatusActions.Continue;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Yes))
		{
			actions |= StorageOperationStatusActions.Yes;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.No))
		{
			actions |= StorageOperationStatusActions.No;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Delete))
		{
			actions |= StorageOperationStatusActions.Delete;
		}

		if (responses.HasFlag(StorageOperationInterruptionResponses.Ok))
		{
			actions |= StorageOperationStatusActions.Ok;
		}

		return actions;
	}

	private static string GetInterruptionTitle(StorageOperationInterruption? interruption)
	{
		return interruption?.Kind switch
		{
			StorageOperationInterruptionKind.InUse => Strings.StorageOperationInUseTitle.GetLocalized(),
			StorageOperationInterruptionKind.AccessDenied => Strings.StorageOperationAccessDeniedTitle.GetLocalized(),
			StorageOperationInterruptionKind.ElevationRequired => Strings.StorageOperationElevationRequiredTitle.GetLocalized(),
			StorageOperationInterruptionKind.DiskFull => Strings.StorageOperationDiskFullTitle.GetLocalized(),
			StorageOperationInterruptionKind.NotFound => Strings.StorageOperationNotFoundTitle.GetLocalized(),
			StorageOperationInterruptionKind.ReadOnly => Strings.StorageOperationReadOnlyTitle.GetLocalized(),
			StorageOperationInterruptionKind.NameConflict => Strings.StorageOperationNameConflictTitle.GetLocalized(),
			StorageOperationInterruptionKind.Unexpected => Strings.StorageOperationUnexpectedTitle.GetLocalized(),
			_ => string.Empty,
		};
	}

	private static string GetInterruptionMessage(StorageOperationInterruption? interruption)
	{
		return interruption?.Kind switch
		{
			StorageOperationInterruptionKind.InUse => Strings.StorageOperationInUseMessage.GetLocalized(),
			StorageOperationInterruptionKind.AccessDenied => Strings.StorageOperationAccessDeniedMessage.GetLocalized(),
			StorageOperationInterruptionKind.ElevationRequired => Strings.StorageOperationElevationRequiredMessage.GetLocalized(),
			StorageOperationInterruptionKind.DiskFull => Strings.StorageOperationDiskFullMessage.GetLocalized(),
			StorageOperationInterruptionKind.NotFound => Strings.StorageOperationNotFoundMessage.GetLocalized(),
			StorageOperationInterruptionKind.ReadOnly => Strings.StorageOperationReadOnlyMessage.GetLocalized(),
			StorageOperationInterruptionKind.NameConflict => Strings.StorageOperationNameConflictMessage.GetLocalized(),
			StorageOperationInterruptionKind.Unexpected => Strings.StorageOperationUnexpectedMessage.GetLocalized(),
			_ => string.Empty,
		};
	}

	private static double GetProgressPercentage(StorageOperationStatus operation)
	{
		if (operation.IsByteProgressForWholeOperation && operation.CompletedBytes is { } aggregateCompletedBytes && operation.TotalBytes is > 0 and { } aggregateTotalBytes)
		{
			return Math.Clamp((double)aggregateCompletedBytes * 100d / aggregateTotalBytes, 0, 100);
		}

		var currentItemProgress = operation.CompletedBytes is { } completedBytes && operation.TotalBytes is > 0 and { } totalBytes
			? Math.Clamp((double)completedBytes / totalBytes, 0, 1)
			: 0;

		return Math.Clamp((operation.CompletedItems + currentItemProgress) * 100d / operation.TotalItems, 0, 100);
	}

	private static string GetCurrentItemText(StorageOperationStatus operation)
	{
		return string.IsNullOrWhiteSpace(operation.CurrentItemName)
			? string.Empty
			: string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationCurrentItemFormat.GetLocalized(), operation.CurrentItemName);
	}

	private static string GetTransferText(StorageOperationStatus operation)
	{
		if (operation.CompletedBytes is { } completedBytes && operation.TotalBytes is { } totalBytes)
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferredFormat.GetLocalized(), FormatBytes(completedBytes), FormatBytes(totalBytes));
		}

		return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), operation.CompletedItems, operation.TotalItems);
	}

	private static string GetSpeedText(StorageOperationStatus operation)
	{
		if (IsTransferPausedState(operation.State))
		{
			return Strings.StorageOperationSpeedUnavailable.GetLocalized();
		}

		return operation.BytesPerSecond is > 0 and { } bytesPerSecond
			? string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationSpeedFormat.GetLocalized(), FormatBytes((long)bytesPerSecond))
			: Strings.StorageOperationSpeedUnavailable.GetLocalized();
	}

	private static string GetRemainingText(StorageOperationStatus operation)
	{
		if (!IsTransferActiveState(operation.State) || operation.RemainingTime is not { } remainingTime)
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

	private static string GetProgressText(StorageOperationStatus operation)
	{
		if (IsActiveState(operation.State) && operation.CompletedBytes is { } completedBytes && operation.TotalBytes is { } totalBytes)
		{
			var completedText = FormatBytes(completedBytes);
			var totalText = FormatBytes(totalBytes);
			if (operation.BytesPerSecond is > 0 and { } bytesPerSecond && operation.RemainingTime is { } remainingTime)
			{
				return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferWithSpeedFormat.GetLocalized(), completedText, totalText, FormatBytes((long)bytesPerSecond),
					FormatRemainingTime(remainingTime));
			}

			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationTransferFormat.GetLocalized(), completedText, totalText);
		}

		return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), operation.CompletedItems, operation.TotalItems);
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

	private static string GetTitle(StorageOperationStatus operation)
	{
		if (IsActiveState(operation.State))
		{
			var currentItemNumber = Math.Min(operation.CompletedItems + 1, operation.TotalItems);
			var progressPercentage = (int)Math.Round(GetProgressPercentage(operation), MidpointRounding.AwayFromZero);
			var format = (operation.Kind, IsTransferPausedState(operation.State)) switch
			{
				(TrackedStorageOperationKind.Copy, false) => Strings.CopyingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Move, false) => Strings.MovingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Delete, false) => Strings.DeletingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Copy, true) => Strings.PausedCopyingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Move, true) => Strings.PausedMovingItemProgressFormat.GetLocalized(),
				(TrackedStorageOperationKind.Delete, true) => Strings.PausedDeletingItemProgressFormat.GetLocalized(),
				_ => Strings.StorageOperationProgressFormat.GetLocalized(),
			};

			return string.Format(CultureInfo.CurrentCulture, format, currentItemNumber, operation.TotalItems, progressPercentage);
		}

		return (operation.Kind, operation.State) switch
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
			(_, TrackedStorageOperationState.Succeeded) => Strings.StorageOperationCompleted.GetLocalized(),
			(_, TrackedStorageOperationState.Failed) => Strings.StorageOperationFailed.GetLocalized(),
			_ => Strings.OperationCanceled.GetLocalized(),
		};
	}

	private static string GetDetail(StorageOperationStatus operation)
	{
		if (operation.State is TrackedStorageOperationState.Failed)
		{
			return operation.Error?.Message ?? Strings.StorageOperationFailed.GetLocalized();
		}

		if (operation.State is TrackedStorageOperationState.Canceled)
		{
			return Strings.OperationCanceled.GetLocalized();
		}

		if (operation.IsCancellationRequested)
		{
			return operation.IsCancellationAcknowledged ? Strings.CancelingOperation.GetLocalized() : Strings.WaitingForCancellationOperation.GetLocalized();
		}

		if (operation.State is TrackedStorageOperationState.Pausing)
		{
			return Strings.PausingOperation.GetLocalized();
		}

		if (operation.State is TrackedStorageOperationState.Resuming)
		{
			return Strings.ResumingOperation.GetLocalized();
		}

		if (operation.State is TrackedStorageOperationState.Succeeded)
		{
			return GetCompletionDetail(operation);
		}

		if (!string.IsNullOrWhiteSpace(operation.DestinationPath))
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationDestinationFormat.GetLocalized(), operation.DestinationPath);
		}

		return operation.CurrentItemName ?? string.Empty;
	}

	private static string GetCompletionDetail(StorageOperationStatus operation)
	{
		var itemCount = string.Format(CultureInfo.CurrentCulture, operation.TotalItems is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized(), operation.TotalItems);

		return operation.Kind switch
		{
			TrackedStorageOperationKind.Copy when !string.IsNullOrWhiteSpace(operation.DestinationPath) => string.Format(CultureInfo.CurrentCulture,
				Strings.CopyCompletedDetailFormat.GetLocalized(), itemCount, operation.DestinationPath),
			TrackedStorageOperationKind.Move when !string.IsNullOrWhiteSpace(operation.DestinationPath) => string.Format(CultureInfo.CurrentCulture,
				Strings.MoveCompletedDetailFormat.GetLocalized(), itemCount, operation.DestinationPath),
			TrackedStorageOperationKind.Delete => string.Format(CultureInfo.CurrentCulture, Strings.DeleteCompletedDetailFormat.GetLocalized(), itemCount),
			_ => string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), operation.CompletedItems, operation.TotalItems),
		};
	}
}

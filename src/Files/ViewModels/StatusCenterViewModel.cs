// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
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
		Items.Clear();
		foreach (var snapshot in snapshots)
		{
			Items.Add(new StatusCenterItemViewModel(snapshot));
		}

		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasCompletedItems));
		OnPropertyChanged(nameof(HasInProgressItems));
		OnPropertyChanged(nameof(InProgressItemCount));
		OnPropertyChanged(nameof(AverageProgressPercentage));
	}
}

public sealed class StatusCenterItemViewModel
{
	public Guid Id { get; }

	public string Title { get; }

	public string Detail { get; }

	public string ProgressText { get; }

	public string Glyph { get; }

	public double ProgressPercentage { get; }

	public bool IsRunning { get; }

	public bool CanCancel { get; }

	public bool CanRemove => !IsRunning;

	internal StatusCenterItemViewModel(StorageOperationSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		Id = snapshot.Id;
		Title = GetTitle(snapshot.Kind, snapshot.State);
		Detail = GetDetail(snapshot);
		ProgressText = string.Format(CultureInfo.CurrentCulture, Strings.StorageOperationProgressFormat.GetLocalized(), snapshot.CompletedItems, snapshot.TotalItems);
		Glyph = GetGlyph(snapshot.State);
		ProgressPercentage = Math.Clamp(snapshot.CompletedItems * 100d / snapshot.TotalItems, 0, 100);
		IsRunning = snapshot.State is TrackedStorageOperationState.Running;
		CanCancel = IsRunning && snapshot.CanCancel && !snapshot.IsCancellationRequested;
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

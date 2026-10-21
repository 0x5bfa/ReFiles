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

	public void ResolveInterruption(Guid operationId, StorageOperationInterruptionDecision decision, bool applyToAll)
	{
		var item = Items.FirstOrDefault(candidate => candidate.Id == operationId);
		if (item is not null)
		{
			_tracker.ResolveInterruption(operationId, item.InterruptionId, new StorageOperationInterruptionResponse(decision, applyToAll));
		}
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
		var operations = _tracker.GetOperations().OrderBy(static operation => IsActiveState(operation.State) ? 0 : 1).ToArray();
		var operationIds = operations.Select(static operation => operation.Id).ToHashSet();
		for (var index = Items.Count - 1; index >= 0; index--)
		{
			if (!operationIds.Contains(Items[index].Id))
			{
				Items.RemoveAt(index);
			}
		}

		for (var index = 0; index < operations.Length; index++)
		{
			var operation = operations[index];
			var item = Items.FirstOrDefault(candidate => candidate.Id == operation.Id);
			if (item is null)
			{
				Items.Insert(index, new StatusCenterItemViewModel(operation));

				continue;
			}

			var currentIndex = Items.IndexOf(item);
			if (currentIndex != index)
			{
				Items.Move(currentIndex, index);
			}

			item.Update(operation);
		}

		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasCompletedItems));
		OnPropertyChanged(nameof(HasInProgressItems));
		OnPropertyChanged(nameof(InProgressItemCount));
		OnPropertyChanged(nameof(AverageProgressPercentage));
	}

	private static bool IsActiveState(TrackedStorageOperationState state) => state is TrackedStorageOperationState.Running or TrackedStorageOperationState.Pausing or TrackedStorageOperationState.Paused
		or TrackedStorageOperationState.Resuming or TrackedStorageOperationState.WaitingForUser;
}

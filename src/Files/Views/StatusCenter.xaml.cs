// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Files.Core.Storage;
using Files.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.Views;

public sealed partial class StatusCenter : UserControl
{
	public static readonly DependencyProperty ViewModelProperty =
		DependencyProperty.Register(nameof(ViewModel), typeof(StatusCenterViewModel), typeof(StatusCenter), new PropertyMetadata(null));

	public StatusCenterViewModel? ViewModel
	{
		get => (StatusCenterViewModel?)GetValue(ViewModelProperty);
		set => SetValue(ViewModelProperty, value);
	}

	public StatusCenter()
	{
		InitializeComponent();
	}

	private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
	{
		ViewModel?.ClearCompleted();
	}

	private void StatusItem_ActionInvoked(object? sender, StorageOperationStatusActionEventArgs e)
	{
		if (sender is not StorageOperationStatusItem { Tag: Guid operationId })
		{
			return;
		}

		switch (e.Action)
		{
			case StorageOperationStatusAction.Expand:
			case StorageOperationStatusAction.Collapse:
				ViewModel?.ToggleExpanded(operationId);
				break;
			case StorageOperationStatusAction.Pause:
			case StorageOperationStatusAction.Resume:
				ViewModel?.TogglePaused(operationId);
				break;
			case StorageOperationStatusAction.Cancel when ViewModel?.Items.FirstOrDefault(item => item.Id == operationId)?.IsWaitingForUser is true:
				ViewModel.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Cancel, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Cancel:
				ViewModel?.Cancel(operationId);
				break;
			case StorageOperationStatusAction.Remove:
				ViewModel?.Remove(operationId);
				break;
			case StorageOperationStatusAction.Retry:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Retry, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Skip:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Skip, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Continue:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Continue, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Yes:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Yes, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.No:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.No, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Delete:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Delete, e.ApplyToAll);
				break;
			case StorageOperationStatusAction.Ok:
				ViewModel?.ResolveInterruption(operationId, StorageOperationInterruptionDecision.Ok, e.ApplyToAll);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(e));
		}
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Automation;

namespace Files.Controls;

/// <summary>Displays one storage operation and any user decision required to continue it.</summary>
public sealed partial class StorageOperationStatusItem : UserControl
{
	[GeneratedDependencyProperty(DefaultValue = StorageOperationStatusKind.Copy)]
	public partial StorageOperationStatusKind OperationKind { get; set; }

	[GeneratedDependencyProperty(DefaultValue = StorageOperationStatusState.Running)]
	public partial StorageOperationStatusState State { get; set; }

	[GeneratedDependencyProperty(DefaultValue = StorageOperationStatusActions.None)]
	public partial StorageOperationStatusActions AvailableActions { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string Title { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string Detail { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string CurrentItemText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string TransferText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string RemainingText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = 0d)]
	public partial double ProgressPercentage { get; set; }

	[GeneratedDependencyProperty(DefaultValue = true)]
	public partial bool IsExpanded { get; set; }

	[GeneratedDependencyProperty(DefaultValue = false)]
	public partial bool CanPause { get; set; }

	[GeneratedDependencyProperty(DefaultValue = false)]
	public partial bool CanCancel { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string InterruptionTitle { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string InterruptionMessage { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string InterruptionItemName { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string InterruptionItemDetails { get; set; }

	[GeneratedDependencyProperty(DefaultValue = false)]
	public partial bool CanApplyToAll { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string PauseButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string ResumeButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string ExpandButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string CollapseButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string RemoveButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string RetryButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string SkipButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string CancelButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string ContinueButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string YesButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string NoButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string DeleteButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string OkButtonText { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "")]
	public partial string ApplyToAllText { get; set; }

	/// <summary>Occurs when the user invokes an operation or interruption action.</summary>
	public event EventHandler<StorageOperationStatusActionEventArgs>? ActionInvoked;

	/// <summary>Initializes a storage operation status item.</summary>
	public StorageOperationStatusItem()
	{
		InitializeComponent();
		UpdatePresentation();
	}

	partial void OnOperationKindChanged(StorageOperationStatusKind newValue) => UpdatePresentation();

	partial void OnStateChanged(StorageOperationStatusState newValue)
	{
		ApplyToAllCheckBox.IsChecked = false;
		UpdatePresentation();
	}

	partial void OnAvailableActionsChanged(StorageOperationStatusActions newValue) => UpdatePresentation();

	partial void OnIsExpandedChanged(bool newValue) => UpdatePresentation();

	partial void OnCanPauseChanged(bool newValue) => UpdatePresentation();

	partial void OnCanCancelChanged(bool newValue) => UpdatePresentation();

	partial void OnCanApplyToAllChanged(bool newValue) => UpdatePresentation();

	partial void OnDetailChanged(string newValue) => UpdatePresentation();

	partial void OnCurrentItemTextChanged(string newValue) => UpdatePresentation();

	partial void OnRemainingTextChanged(string newValue) => UpdatePresentation();

	partial void OnInterruptionItemNameChanged(string newValue) => UpdatePresentation();

	partial void OnInterruptionItemDetailsChanged(string newValue) => UpdatePresentation();

	partial void OnPauseButtonTextChanged(string newValue) => UpdateAutomationNames();

	partial void OnResumeButtonTextChanged(string newValue) => UpdateAutomationNames();

	partial void OnExpandButtonTextChanged(string newValue) => UpdateAutomationNames();

	partial void OnCollapseButtonTextChanged(string newValue) => UpdateAutomationNames();

	partial void OnRemoveButtonTextChanged(string newValue) => UpdateAutomationNames();

	private static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

	private void RaiseAction(StorageOperationStatusAction action, bool includeApplyToAll = false)
	{
		ActionInvoked?.Invoke(this, new StorageOperationStatusActionEventArgs(action, includeApplyToAll && ApplyToAllCheckBox.IsChecked is true));
	}

	private void UpdatePresentation()
	{
		if (ProgressPane is null)
		{
			return;
		}

		var isActive = State is StorageOperationStatusState.Running or StorageOperationStatusState.Pausing or StorageOperationStatusState.Paused or StorageOperationStatusState.Resuming
			or StorageOperationStatusState.WaitingForUser;
		var isWaitingForUser = State is StorageOperationStatusState.WaitingForUser;
		var isPaused = State is StorageOperationStatusState.Paused or StorageOperationStatusState.Resuming;
		var isTransferring = State is StorageOperationStatusState.Running or StorageOperationStatusState.Pausing;
		var operationIconEnabled = isTransferring;

		CopyIcon.Visibility = ToVisibility(isActive && OperationKind is StorageOperationStatusKind.Copy);
		MoveIcon.Visibility = ToVisibility(isActive && OperationKind is StorageOperationStatusKind.Move);
		DeleteIcon.Visibility = ToVisibility(isActive && OperationKind is StorageOperationStatusKind.Delete);
		CopyIcon.IsEnabled = operationIconEnabled;
		MoveIcon.IsEnabled = operationIconEnabled;
		DeleteIcon.IsEnabled = operationIconEnabled;
		ActiveIconBackground.Visibility = ToVisibility(isActive && !isPaused && !isWaitingForUser);
		DisabledIconBackground.Visibility = ToVisibility(isActive && (isPaused || isWaitingForUser));
		SucceededIcon.Visibility = ToVisibility(State is StorageOperationStatusState.Succeeded);
		FailedIcon.Visibility = ToVisibility(State is StorageOperationStatusState.Failed);
		CanceledIcon.Visibility = ToVisibility(State is StorageOperationStatusState.Canceled);
		SucceededIconBackground.Visibility = SucceededIcon.Visibility;
		FailedIconBackground.Visibility = FailedIcon.Visibility;
		CanceledIconBackground.Visibility = CanceledIcon.Visibility;

		PauseButton.Visibility = ToVisibility(isActive && !isWaitingForUser && CanPause);
		ExpandButton.Visibility = ToVisibility(isActive && !isWaitingForUser);
		RemoveButton.Visibility = ToVisibility(!isActive);
		ProgressPane.Visibility = ToVisibility(isActive && !isWaitingForUser);
		InterruptionPane.Visibility = ToVisibility(isWaitingForUser);
		RunningProgressBar.Visibility = ToVisibility(isTransferring);
		PausedProgressBar.Visibility = ToVisibility(isPaused);
		ExpandedDetails.Visibility = ToVisibility(IsExpanded);
		CurrentItemTextBlock.Visibility = ToVisibility(IsExpanded && !string.IsNullOrWhiteSpace(CurrentItemText));
		RemainingTextBlock.Visibility = ToVisibility(IsExpanded && !string.IsNullOrWhiteSpace(RemainingText));
		ProgressCancelButton.Visibility = ToVisibility(CanCancel);
		DetailTextBlock.Visibility = ToVisibility(!string.IsNullOrWhiteSpace(Detail));

		InterruptionItemPanel.Visibility = ToVisibility(!string.IsNullOrWhiteSpace(InterruptionItemName) || !string.IsNullOrWhiteSpace(InterruptionItemDetails));
		InterruptionItemNameBlock.Visibility = ToVisibility(!string.IsNullOrWhiteSpace(InterruptionItemName));
		InterruptionItemDetailsBlock.Visibility = ToVisibility(!string.IsNullOrWhiteSpace(InterruptionItemDetails));
		ApplyToAllCheckBox.Visibility = ToVisibility(isWaitingForUser && CanApplyToAll);
		ContinueButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Continue));
		DeleteResponseButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Delete));
		RetryButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Retry));
		YesButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Yes));
		OkButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Ok));
		SkipButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Skip));
		NoButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.No));
		InterruptionCancelButton.Visibility = ToVisibility(AvailableActions.HasFlag(StorageOperationStatusActions.Cancel));
		UpdateAutomationNames();
	}

	private void UpdateAutomationNames()
	{
		if (PauseButton is null)
		{
			return;
		}

		var pauseName = State is StorageOperationStatusState.Pausing or StorageOperationStatusState.Paused ? ResumeButtonText : PauseButtonText;
		var expandName = IsExpanded ? CollapseButtonText : ExpandButtonText;
		AutomationProperties.SetName(PauseButton, pauseName);
		AutomationProperties.SetName(ExpandButton, expandName);
		AutomationProperties.SetName(RemoveButton, RemoveButtonText);
		ToolTipService.SetToolTip(PauseButton, pauseName);
		ToolTipService.SetToolTip(ExpandButton, expandName);
		ToolTipService.SetToolTip(RemoveButton, RemoveButtonText);
		PauseGlyph.Glyph = State is StorageOperationStatusState.Pausing or StorageOperationStatusState.Paused ? "\uE768" : "\uE769";
		ExpandGlyph.Glyph = IsExpanded ? "\uE70E" : "\uE70D";
	}

	private void PauseButton_Click(object sender, RoutedEventArgs e)
	{
		RaiseAction(State is StorageOperationStatusState.Pausing or StorageOperationStatusState.Paused ? StorageOperationStatusAction.Resume : StorageOperationStatusAction.Pause);
	}

	private void ExpandButton_Click(object sender, RoutedEventArgs e)
	{
		RaiseAction(IsExpanded ? StorageOperationStatusAction.Collapse : StorageOperationStatusAction.Expand);
	}

	private void RemoveButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Remove);

	private void ProgressCancelButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Cancel);

	private void ContinueButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Continue, includeApplyToAll: true);

	private void DeleteResponseButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Delete, includeApplyToAll: true);

	private void RetryButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Retry, includeApplyToAll: true);

	private void YesButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Yes, includeApplyToAll: true);

	private void OkButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Ok, includeApplyToAll: true);

	private void SkipButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Skip, includeApplyToAll: true);

	private void NoButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.No, includeApplyToAll: true);

	private void InterruptionCancelButton_Click(object sender, RoutedEventArgs e) => RaiseAction(StorageOperationStatusAction.Cancel, includeApplyToAll: true);
}

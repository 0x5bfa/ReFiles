// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace Files.ControlTests.Views;

public sealed partial class StorageOperationStatusPage : Page
{
	private const string DefaultCurrentItemText = "File: “Windows11_InsiderPreview_Client_x64_en-gb_29634_1000.iso”";
	private const string DefaultDetail = @"to D:\Test destination";
	private const string DefaultTitle = "Copying item 3 of 5 (64%)";

	public IReadOnlyList<StatusOperationScenario> Scenarios { get; } = CreateScenarios();

	public StorageOperationStatusPage()
	{
		InitializeComponent();
		ApplyScenario(Scenarios[0]);
	}

	private static IReadOnlyList<StatusOperationScenario> CreateScenarios()
	{
		const StorageOperationStatusActions standardActions = StorageOperationStatusActions.Retry | StorageOperationStatusActions.Skip | StorageOperationStatusActions.Cancel;

		return
		[
			new("Running expanded", StorageOperationStatusState.Running, StorageOperationStatusKind.Copy, DefaultTitle, DefaultDetail, 64, true, true, true),
			new("Running compact", StorageOperationStatusState.Running, StorageOperationStatusKind.Move, "Moving item 3 of 5 (64%)", DefaultDetail, 64, false, true, true),
			new("Pausing", StorageOperationStatusState.Pausing, StorageOperationStatusKind.Copy, DefaultTitle, "Waiting for Windows to pause...", 64, true, true, true),
			new("Paused", StorageOperationStatusState.Paused, StorageOperationStatusKind.Copy, "Paused copying item 3 of 5 (64%)", DefaultDetail, 64, true, true, true),
			new("Resuming", StorageOperationStatusState.Resuming, StorageOperationStatusKind.Copy, "Paused copying item 3 of 5 (64%)", "Waiting for Windows to resume...", 64, true, true, true),
			new("File in use", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Copy, DefaultTitle, DefaultDetail, 64, true, false, false, standardActions, true,
				"File in use", "The action can't be completed because the file is open in another program. Close the file and try again."),
			new("File in use by an identified app", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Copy, DefaultTitle, DefaultDetail, 64, true, false, false, standardActions, true,
				"File in use", "The action can't be completed because the file is open in Microsoft Defender Antivirus. Close the file and try again."),
			new("Access denied", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Delete, "Deleting item 2 of 4 (25%)", "secure-document.txt", 25, true, false, false,
				standardActions, true, "You need permission to perform this action", "Check the item's permissions and try again.", "secure-document.txt", @"C:\Protected"),
			new("Elevation required", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Move, "Moving item 1 of 3 (10%)", @"to C:\Program Files", 10, true, false, false,
				StorageOperationStatusActions.Continue | StorageOperationStatusActions.Skip | StorageOperationStatusActions.Cancel, true, "Administrator permission is required",
				"Select Continue to complete this action with administrator rights.", "application.exe", @"C:\Program Files"),
			new("Disk full", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Copy, DefaultTitle, @"to E:\Backups", 64, true, false, false, standardActions, true,
				"There is not enough space", "Free some space on the destination drive and try again.", "large-backup.iso", @"E:\Backups"),
			new("Item not found", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Move, "Moving item 4 of 8 (50%)", DefaultDetail, 50, true, false, false, standardActions, true,
				"Could not find this item", "The item is no longer in its original location. Verify the location and try again.", "missing.txt", @"D:\Source"),
			new("Read-only item", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Delete, "Deleting item 1 of 2 (0%)", "readonly.txt", 0, true, false, false, standardActions, true,
				"This item is read-only", "The item cannot be modified. Change its properties and try again.", "readonly.txt", @"D:\Documents"),
			new("Name conflict", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Copy, "Copying item 1 of 3 (0%)", DefaultDetail, 0, true, false, false,
				StorageOperationStatusActions.Yes | StorageOperationStatusActions.No | StorageOperationStatusActions.Cancel, true, "An item with the same name already exists",
				"Choose Yes to replace or merge the destination item, or No to skip this item.", "report.docx", @"D:\Test destination\report.docx"),
			new("Unexpected error", StorageOperationStatusState.WaitingForUser, StorageOperationStatusKind.Copy, DefaultTitle, DefaultDetail, 64, true, false, false, standardActions, false,
				"The action can't be completed", "Resolve the problem and try again, skip this item, or cancel the operation."),
			new("Succeeded", StorageOperationStatusState.Succeeded, StorageOperationStatusKind.Move, "Move completed", @"Successfully moved 5 items to D:\Test destination", 100, false, false, false),
			new("Failed", StorageOperationStatusState.Failed, StorageOperationStatusKind.Copy, "Copy failed", "The destination is unavailable.", 64, false, false, false),
			new("Canceled", StorageOperationStatusState.Canceled, StorageOperationStatusKind.Delete, "Delete canceled", "The operation was canceled.", 25, false, false, false),
		];
	}

	private void ApplyScenario(StatusOperationScenario scenario)
	{
		InteractiveItem.OperationKind = scenario.OperationKind;
		InteractiveItem.State = scenario.State;
		InteractiveItem.Title = scenario.Title;
		InteractiveItem.Detail = scenario.Detail;
		InteractiveItem.ProgressPercentage = scenario.ProgressPercentage;
		InteractiveItem.IsExpanded = scenario.IsExpanded;
		InteractiveItem.CanPause = scenario.CanPause;
		InteractiveItem.CanCancel = scenario.CanCancel;
		InteractiveItem.AvailableActions = scenario.AvailableActions;
		InteractiveItem.CanApplyToAll = scenario.CanApplyToAll;
		InteractiveItem.InterruptionTitle = scenario.InterruptionTitle;
		InteractiveItem.InterruptionMessage = scenario.InterruptionMessage;
		InteractiveItem.InterruptionItemName = scenario.InterruptionItemName;
		InteractiveItem.InterruptionItemDetails = scenario.InterruptionItemDetails;
		InteractiveItem.CurrentItemText = scenario.CurrentItemText;
		InteractiveItem.TransferText = scenario.TransferText;
		InteractiveItem.RemainingText = scenario.RemainingText;
	}

	private void InteractiveItem_ActionInvoked(object? sender, StorageOperationStatusActionEventArgs e)
	{
		EventLog.Text = $"{DateTime.Now:T}  {e.Action}  ApplyToAll={e.ApplyToAll}{Environment.NewLine}{EventLog.Text}";
		switch (e.Action)
		{
			case StorageOperationStatusAction.Expand:
				InteractiveItem.IsExpanded = true;
				break;
			case StorageOperationStatusAction.Collapse:
				InteractiveItem.IsExpanded = false;
				break;
			case StorageOperationStatusAction.Pause:
				InteractiveItem.State = StorageOperationStatusState.Paused;
				break;
			case StorageOperationStatusAction.Resume:
			case StorageOperationStatusAction.Retry:
			case StorageOperationStatusAction.Continue:
			case StorageOperationStatusAction.Yes:
			case StorageOperationStatusAction.Ok:
				InteractiveItem.State = StorageOperationStatusState.Running;
				InteractiveItem.AvailableActions = StorageOperationStatusActions.None;
				break;
			case StorageOperationStatusAction.Skip:
			case StorageOperationStatusAction.No:
				InteractiveItem.State = StorageOperationStatusState.Succeeded;
				InteractiveItem.Title = "Operation continued after skipping the item";
				break;
			case StorageOperationStatusAction.Cancel:
				InteractiveItem.State = StorageOperationStatusState.Canceled;
				InteractiveItem.Title = "Operation canceled";
				break;
			case StorageOperationStatusAction.Delete:
				InteractiveItem.State = StorageOperationStatusState.Running;
				break;
			case StorageOperationStatusAction.Remove:
				InteractiveItem.Visibility = Visibility.Collapsed;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(e));
		}
	}

	private void GalleryItem_ActionInvoked(object? sender, StorageOperationStatusActionEventArgs e)
	{
		var scenario = (sender as FrameworkElement)?.DataContext as StatusOperationScenario;
		EventLog.Text = $"{DateTime.Now:T}  {scenario?.Name ?? "Gallery item"}: {e.Action}  ApplyToAll={e.ApplyToAll}{Environment.NewLine}{EventLog.Text}";
	}

	private void InteractiveScenarioPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (InteractiveScenarioPicker.SelectedItem is StatusOperationScenario scenario)
		{
			InteractiveItem.Visibility = Visibility.Visible;
			ApplyScenario(scenario);
		}
	}

	private void CardWidthSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (InteractiveItem is not null)
		{
			InteractiveItem.Width = e.NewValue;
		}
	}

	private void TextSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (InteractiveItem is not null)
		{
			InteractiveItem.FontSize = e.NewValue;
		}
	}

	private void RightToLeftToggle_Toggled(object sender, RoutedEventArgs e)
	{
		InteractiveItem.FlowDirection = RightToLeftToggle.IsOn ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
	}

	private void LongTextToggle_Toggled(object sender, RoutedEventArgs e)
	{
		InteractiveItem.Title = LongTextToggle.IsOn ? "Copying a file with an exceptionally long name that must be trimmed without moving any action buttons outside the operation card" : DefaultTitle;
		InteractiveItem.Detail = LongTextToggle.IsOn ? @"to D:\A very long destination folder name\Another very long folder name\A final folder that exceeds the available card width" : DefaultDetail;
		InteractiveItem.CurrentItemText = LongTextToggle.IsOn ? "File: “A very long filename used to verify ellipsis, accessible naming, and layout behavior at narrow widths.iso”" : DefaultCurrentItemText;
	}
}

public sealed class StatusOperationScenario
{
	public string Name { get; }
	public StorageOperationStatusState State { get; }
	public StorageOperationStatusKind OperationKind { get; }
	public string Title { get; }
	public string Detail { get; }
	public double ProgressPercentage { get; }
	public bool IsExpanded { get; }
	public bool CanPause { get; }
	public bool CanCancel { get; }
	public StorageOperationStatusActions AvailableActions { get; }
	public bool CanApplyToAll { get; }
	public string InterruptionTitle { get; }
	public string InterruptionMessage { get; }
	public string InterruptionItemName { get; }
	public string InterruptionItemDetails { get; }
	public string CurrentItemText { get; } = "File: “Windows11_InsiderPreview_Client_x64_en-gb_29634_1000.iso”";
	public string TransferText { get; } = "3.2 GB of 5.0 GB transferred";
	public string RemainingText { get; } = "About 2 minutes remaining";

	public StatusOperationScenario(string name, StorageOperationStatusState state, StorageOperationStatusKind operationKind, string title, string detail, double progressPercentage, bool isExpanded,
		bool canPause, bool canCancel, StorageOperationStatusActions availableActions = StorageOperationStatusActions.None, bool canApplyToAll = false, string interruptionTitle = "",
		string interruptionMessage = "", string interruptionItemName = "Windows11_InsiderPreview_Client_x64_en-gb_29634_1000.iso", string interruptionItemDetails = @"D:\Test destination")
	{
		Name = name;
		State = state;
		OperationKind = operationKind;
		Title = title;
		Detail = detail;
		ProgressPercentage = progressPercentage;
		IsExpanded = isExpanded;
		CanPause = canPause;
		CanCancel = canCancel;
		AvailableActions = availableActions;
		CanApplyToAll = canApplyToAll;
		InterruptionTitle = interruptionTitle;
		InterruptionMessage = interruptionMessage;
		InterruptionItemName = interruptionItemName;
		InterruptionItemDetails = interruptionItemDetails;
	}
}

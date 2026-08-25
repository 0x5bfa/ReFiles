// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;

namespace Files.ViewModels;

public sealed partial class TabStripViewModel : ObservableObject
{
	private readonly Action<int> _setActiveTabAt;

	public ObservableCollection<TabViewModel> Tabs { get; }

	public CommandBindingViewModel NewTabCommand { get; }

	public CommandBindingViewModel CloseTabCommand { get; }

	public CommandBindingViewModel NewPaneCommand { get; }

	public CommandBindingViewModel ClosePaneCommand { get; }

	public CommandBindingViewModel SplitPaneVerticalCommand { get; }

	public CommandBindingViewModel SplitPaneHorizontalCommand { get; }

	[ObservableProperty]
	public partial int ActiveTabIndex { get; private set; } = -1;

	internal TabStripViewModel(
		ObservableCollection<TabViewModel> tabs,
		CommandBindingViewModel newTabCommand,
		CommandBindingViewModel closeTabCommand,
		CommandBindingViewModel newPaneCommand,
		CommandBindingViewModel closePaneCommand,
		CommandBindingViewModel splitPaneVerticalCommand,
		CommandBindingViewModel splitPaneHorizontalCommand,
		Action<int> setActiveTabAt)
	{
		ArgumentNullException.ThrowIfNull(tabs);
		ArgumentNullException.ThrowIfNull(newTabCommand);
		ArgumentNullException.ThrowIfNull(closeTabCommand);
		ArgumentNullException.ThrowIfNull(newPaneCommand);
		ArgumentNullException.ThrowIfNull(closePaneCommand);
		ArgumentNullException.ThrowIfNull(splitPaneVerticalCommand);
		ArgumentNullException.ThrowIfNull(splitPaneHorizontalCommand);
		ArgumentNullException.ThrowIfNull(setActiveTabAt);

		Tabs = tabs;
		NewTabCommand = newTabCommand;
		CloseTabCommand = closeTabCommand;
		NewPaneCommand = newPaneCommand;
		ClosePaneCommand = closePaneCommand;
		SplitPaneVerticalCommand = splitPaneVerticalCommand;
		SplitPaneHorizontalCommand = splitPaneHorizontalCommand;
		_setActiveTabAt = setActiveTabAt;
	}

	public void SetActiveTabAt(int index)
	{
		_setActiveTabAt(index);
	}

	internal void SetActiveTabIndex(int index)
	{
		ActiveTabIndex = index;
	}
}

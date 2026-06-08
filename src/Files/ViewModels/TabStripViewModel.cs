// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;

namespace Files.ViewModels;

public sealed class TabStripViewModel : ObservableObject
{
	private readonly Action<int> setActiveTabAt;
	private int activeTabIndex = -1;

	internal TabStripViewModel(
		ObservableCollection<TabViewModel> tabs,
		CommandBindingViewModel newTabCommand,
		CommandBindingViewModel closeTabCommand,
		Action<int> setActiveTabAt)
	{
		ArgumentNullException.ThrowIfNull(tabs);
		ArgumentNullException.ThrowIfNull(newTabCommand);
		ArgumentNullException.ThrowIfNull(closeTabCommand);
		ArgumentNullException.ThrowIfNull(setActiveTabAt);

		Tabs = tabs;
		NewTabCommand = newTabCommand;
		CloseTabCommand = closeTabCommand;
		this.setActiveTabAt = setActiveTabAt;
	}

	public ObservableCollection<TabViewModel> Tabs { get; }

	public CommandBindingViewModel NewTabCommand { get; }

	public CommandBindingViewModel CloseTabCommand { get; }

	public int ActiveTabIndex
	{
		get => activeTabIndex;
		private set => SetProperty(ref activeTabIndex, value);
	}

	public void SetActiveTabAt(int index) => setActiveTabAt(index);

	internal void SetActiveTabIndex(int index) => ActiveTabIndex = index;
}

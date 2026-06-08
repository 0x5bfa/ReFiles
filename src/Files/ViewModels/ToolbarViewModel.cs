// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Localization;

namespace Files.ViewModels;

public sealed class ToolbarViewModel : ObservableObject, IDisposable
{
	private TabViewModel? activeTab;
	private int isDisposed;

	internal ToolbarViewModel(
		CommandBindingViewModel newPaneCommand,
		CommandBindingViewModel closePaneCommand)
	{
		ArgumentNullException.ThrowIfNull(newPaneCommand);
		ArgumentNullException.ThrowIfNull(closePaneCommand);

		NewPaneCommand = newPaneCommand;
		ClosePaneCommand = closePaneCommand;
	}

	public CommandBindingViewModel NewPaneCommand { get; }

	public CommandBindingViewModel ClosePaneCommand { get; }

	public string ActiveTabTitle => activeTab?.Title ?? Strings.NoTabs.GetLocalized();

	internal void SetActiveTab(TabViewModel? value)
	{
		if (ReferenceEquals(activeTab, value))
		{
			return;
		}

		if (activeTab is not null)
		{
			activeTab.PropertyChanged -= ActiveTab_PropertyChanged;
		}

		activeTab = value;
		if (activeTab is not null)
		{
			activeTab.PropertyChanged += ActiveTab_PropertyChanged;
		}

		OnPropertyChanged(nameof(ActiveTabTitle));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		if (activeTab is not null)
		{
			activeTab.PropertyChanged -= ActiveTab_PropertyChanged;
			activeTab = null;
		}
	}

	private void ActiveTab_PropertyChanged(
		object? sender,
		PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null
			or nameof(TabViewModel.Title))
		{
			OnPropertyChanged(nameof(ActiveTabTitle));
		}
	}
}

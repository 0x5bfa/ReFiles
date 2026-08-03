// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Localization;

namespace Files.ViewModels;

public sealed class ToolbarViewModel : ObservableObject, IDisposable
{
	private TabViewModel? _activeTab;

	private int _isDisposed;

	public CommandBindingViewModel NewPaneCommand { get; }

	public CommandBindingViewModel ClosePaneCommand { get; }

	public CommandBindingViewModel LayoutDetailsCommand { get; }

	public CommandBindingViewModel LayoutListCommand { get; }

	public CommandBindingViewModel LayoutGridCommand { get; }

	public string ActiveTabTitle => _activeTab?.Title ?? Strings.NoTabs.GetLocalized();

	public string LayoutLabel => Strings.Layout.GetLocalized();

	public string LayoutGlyph => _activeTab?.ActivePane?.FolderBrowser.ViewMode switch
	{
		FolderViewMode.List => "\uE8FD",
		FolderViewMode.Grid => "\uECA5",
		_ => "\uE8A9",
	};

	internal ToolbarViewModel(
		CommandBindingViewModel newPaneCommand,
		CommandBindingViewModel closePaneCommand,
		CommandBindingViewModel layoutDetailsCommand,
		CommandBindingViewModel layoutListCommand,
		CommandBindingViewModel layoutGridCommand)
	{
		ArgumentNullException.ThrowIfNull(newPaneCommand);
		ArgumentNullException.ThrowIfNull(closePaneCommand);
		ArgumentNullException.ThrowIfNull(layoutDetailsCommand);
		ArgumentNullException.ThrowIfNull(layoutListCommand);
		ArgumentNullException.ThrowIfNull(layoutGridCommand);

		NewPaneCommand = newPaneCommand;
		ClosePaneCommand = closePaneCommand;
		LayoutDetailsCommand = layoutDetailsCommand;
		LayoutListCommand = layoutListCommand;
		LayoutGridCommand = layoutGridCommand;
	}

	internal void SetActiveTab(TabViewModel? value)
	{
		if (ReferenceEquals(_activeTab, value))
		{
			return;
		}

		if (_activeTab is not null)
		{
			_activeTab.PropertyChanged -= ActiveTab_PropertyChanged;
		}

		_activeTab = value;
		if (_activeTab is not null)
		{
			_activeTab.PropertyChanged += ActiveTab_PropertyChanged;
		}

		OnPropertyChanged(nameof(ActiveTabTitle));
		OnPropertyChanged(nameof(LayoutGlyph));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		if (_activeTab is not null)
		{
			_activeTab.PropertyChanged -= ActiveTab_PropertyChanged;
			_activeTab = null;
		}
	}

	private void ActiveTab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(TabViewModel.Title) or nameof(TabViewModel.ActivePane))
		{
			OnPropertyChanged(nameof(ActiveTabTitle));
			OnPropertyChanged(nameof(LayoutGlyph));
		}
	}
}

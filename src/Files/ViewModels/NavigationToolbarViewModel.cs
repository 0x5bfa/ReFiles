// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Localization;

namespace Files.ViewModels;

public sealed class NavigationToolbarViewModel : ObservableObject, IDisposable
{
	private FolderBrowserViewModel? _activeFolderBrowser;

	private int _isDisposed;

	public CommandBindingViewModel ToggleSidebarCommand { get; }

	public CommandBindingViewModel BackCommand { get; }

	public CommandBindingViewModel ForwardCommand { get; }

	public CommandBindingViewModel UpCommand { get; }

	public CommandBindingViewModel HomeCommand { get; }

	public CommandBindingViewModel NavigatePathCommand { get; }

	public CommandBindingViewModel RefreshCommand { get; }

	public string PathPlaceholderText => Strings.EnterFolderPath.GetLocalized();

	public string LocationText => _activeFolderBrowser?.LocationText ?? string.Empty;

	internal NavigationToolbarViewModel(
		CommandBindingViewModel toggleSidebarCommand,
		CommandBindingViewModel backCommand,
		CommandBindingViewModel forwardCommand,
		CommandBindingViewModel upCommand,
		CommandBindingViewModel homeCommand,
		CommandBindingViewModel navigatePathCommand,
		CommandBindingViewModel refreshCommand)
	{
		ArgumentNullException.ThrowIfNull(toggleSidebarCommand);
		ArgumentNullException.ThrowIfNull(backCommand);
		ArgumentNullException.ThrowIfNull(forwardCommand);
		ArgumentNullException.ThrowIfNull(upCommand);
		ArgumentNullException.ThrowIfNull(homeCommand);
		ArgumentNullException.ThrowIfNull(navigatePathCommand);
		ArgumentNullException.ThrowIfNull(refreshCommand);

		ToggleSidebarCommand = toggleSidebarCommand;
		BackCommand = backCommand;
		ForwardCommand = forwardCommand;
		UpCommand = upCommand;
		HomeCommand = homeCommand;
		NavigatePathCommand = navigatePathCommand;
		RefreshCommand = refreshCommand;
	}

	internal void SetActiveFolderBrowser(FolderBrowserViewModel? value)
	{
		if (ReferenceEquals(_activeFolderBrowser, value))
		{
			return;
		}

		_activeFolderBrowser?.PropertyChanged -= ActiveFolderBrowser_PropertyChanged;
		_activeFolderBrowser = value;
		_activeFolderBrowser?.PropertyChanged += ActiveFolderBrowser_PropertyChanged;

		OnPropertyChanged(nameof(LocationText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_activeFolderBrowser?.PropertyChanged -= ActiveFolderBrowser_PropertyChanged;
		_activeFolderBrowser = null;
	}

	private void ActiveFolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is null or nameof(FolderBrowserViewModel.LocationText))
		{
			OnPropertyChanged(nameof(LocationText));
		}
	}
}

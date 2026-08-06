// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Core.Sessions;
using Files.Presentation;

namespace Files.ViewModels;

public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
	private readonly PaneSession _pane;

	private int _isDisposed;

	public Guid Id => _pane.Id;

	public FolderBrowserViewModel FolderBrowser { get; }

	public object Content => FolderBrowser;

	[ObservableProperty]
	public partial bool IsActive { get; private set; }

	public string Title => FolderBrowser.LocationText;

	public string StatusText => FolderBrowser.StatusText;

	public bool IsLoading => FolderBrowser.IsBusy;

	public bool CanGoBack => FolderBrowser.CanGoBack;

	public bool CanGoForward => FolderBrowser.CanGoForward;

	public bool CanGoUp => FolderBrowser.CanGoUp;

	public bool CanRefresh => FolderBrowser.CanRefresh;

	internal PaneViewModel(PaneSession pane, WindowPresentationFactory presentationFactory, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(pane);
		ArgumentNullException.ThrowIfNull(presentationFactory);
		ArgumentNullException.ThrowIfNull(commandManager);

		_pane = pane;
		FolderBrowser = pane.Content is BrowsePaneSession browsePane
			? presentationFactory.CreateFolderBrowser(browsePane, commandManager)
			: throw new NotSupportedException($"Pane content '{pane.Content.GetType().Name}' has no presentation mapping.");
		FolderBrowser.PropertyChanged += FolderBrowser_PropertyChanged;
	}

	public void SetActive(bool value)
	{
		if (IsActive != value)
		{
			IsActive = value;
			OnPropertyChanged(nameof(Title));
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		FolderBrowser.PropertyChanged -= FolderBrowser_PropertyChanged;
		FolderBrowser.Dispose();
	}

	private void FolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(FolderBrowserViewModel.LocationText):
				OnPropertyChanged(nameof(Title));
				break;
			case nameof(FolderBrowserViewModel.StatusText):
				OnPropertyChanged(nameof(StatusText));
				break;
			case nameof(FolderBrowserViewModel.IsLoading):
			case nameof(FolderBrowserViewModel.IsBusy):
				OnPropertyChanged(nameof(IsLoading));
				break;
			case nameof(FolderBrowserViewModel.CanGoBack):
				OnPropertyChanged(nameof(CanGoBack));
				break;
			case nameof(FolderBrowserViewModel.CanGoForward):
				OnPropertyChanged(nameof(CanGoForward));
				break;
			case nameof(FolderBrowserViewModel.CanGoUp):
				OnPropertyChanged(nameof(CanGoUp));
				break;
			case nameof(FolderBrowserViewModel.CanRefresh):
				OnPropertyChanged(nameof(CanRefresh));
				break;
		}
	}
}

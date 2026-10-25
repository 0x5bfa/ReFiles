// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Core.Sessions;
using Files.Presentation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public sealed partial class PaneViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
	private readonly PaneSession _pane;
	private readonly PaneContentPresentation _presentation;

	private int _isDisposed;

	public Guid Id => _pane.Id;

	public FolderBrowserViewModel? FolderBrowser => _presentation.FolderBrowser;

	public PreviewPaneViewModel? Preview => _presentation.Preview;

	public object Content => _presentation.Content;

	public bool HasBrowseContent => FolderBrowser is not null;

	public bool HasPaneChrome => _presentation.HasPaneChrome;

	public IconSource? IconSource => _presentation.IconSource;

	[ObservableProperty]
	public partial bool IsActive { get; private set; }

	public string Title => _presentation.Title;

	public ImageSource? Icon => _presentation.Icon;

	public string StatusText => FolderBrowser?.StatusText ?? string.Empty;

	public bool IsLoading => FolderBrowser?.IsBusy ?? false;

	public bool CanGoBack => FolderBrowser?.CanGoBack ?? false;

	public bool CanGoForward => FolderBrowser?.CanGoForward ?? false;

	public bool CanGoUp => FolderBrowser?.CanGoUp ?? false;

	public bool CanRefresh => FolderBrowser?.CanRefresh ?? false;

	internal event EventHandler<OperationErrorEventArgs>? OperationErrorReported;

	internal PaneViewModel(PaneSession pane, WindowPresentationFactory presentationFactory, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(pane);

		ArgumentNullException.ThrowIfNull(presentationFactory);

		ArgumentNullException.ThrowIfNull(commandManager);

		_pane = pane;
		_presentation = presentationFactory.CreatePaneContent(pane.Content, commandManager);
		if (FolderBrowser is { } browser)
		{
			browser.PropertyChanged += FolderBrowser_PropertyChanged;
			browser.OperationErrorReported += FolderBrowser_OperationErrorReported;
		}

	}

	internal UIElement CreateContentView()
	{
		return _presentation.CreateView();
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
		_ = DisposeAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		if (FolderBrowser is { } browser)
		{
			browser.PropertyChanged -= FolderBrowser_PropertyChanged;
			browser.OperationErrorReported -= FolderBrowser_OperationErrorReported;
		}

		await _presentation.DisposeAsync().ConfigureAwait(false);
	}

	private void FolderBrowser_OperationErrorReported(object? sender, OperationErrorEventArgs e)
	{
		OperationErrorReported?.Invoke(this, e);
	}

	private void FolderBrowser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(FolderBrowserViewModel.LocationDisplayName):
				OnPropertyChanged(nameof(Title));
				break;
			case nameof(FolderBrowserViewModel.LocationIcon):
				OnPropertyChanged(nameof(Icon));
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

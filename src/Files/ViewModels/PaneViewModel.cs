// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Infrastructure;
using Files.Core.AppModels;
using Files.Core.Data;

namespace Files.ViewModels;

public enum PaneContentKind
{
	FolderBrowser,
	Settings,
	Web,
}

public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
	private readonly PaneModel _pane;

	private int _isDisposed;

	public Guid Id => _pane.Id;

	public FolderBrowserViewModel FolderBrowser { get; }

	[ObservableProperty]
	public partial PaneContentKind ContentKind { get; private set; } = PaneContentKind.FolderBrowser;

	[ObservableProperty]
	public partial bool IsActive { get; private set; }

	public string Title => FolderBrowser.LocationText;

	public string StatusText => FolderBrowser.StatusText;

	public PaneViewModel(PaneModel pane, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(pane);

		_pane = pane;
		FolderBrowser = new(pane, dataRoot, dispatcher, commandManager);
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

	public void SetContentKind(PaneContentKind kind)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		ContentKind = kind;
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
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(StatusText));
	}
}

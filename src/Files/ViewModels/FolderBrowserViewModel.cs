// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Adapters;
using Files.Commands;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.AppModels;
using Files.Core.Browsing;
using Files.Core.Data;

namespace Files.ViewModels;

public enum FolderViewMode
{
	Details,
	Grid,
	List,
}

public sealed class FolderBrowserViewModel : ObservableObject, IDisposable
{
	private readonly CoreBrowseAdapter browseAdapter;
	private string? operationError;
	private bool isApplyingUpdate;
	private int isDisposed;
	private FolderViewMode viewMode = FolderViewMode.Details;

	public FolderBrowserViewModel(
		PaneModel pane,
		IFilesDataRoot dataRoot,
		IUIDispatcher dispatcher,
		WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(commandManager);
		CommandManager = commandManager;
		browseAdapter = new CoreBrowseAdapter(pane, dataRoot, dispatcher);
		browseAdapter.Updated += BrowseAdapter_Updated;
	}

	internal WindowCommandManager CommandManager { get; }

	public ObservableCollection<BrowseItemViewModel> Items { get; } = [];

	public FolderViewMode ViewMode
	{
		get => viewMode;
		private set => SetProperty(ref viewMode, value);
	}

	public bool IsApplyingUpdate => isApplyingUpdate;

	public IReadOnlyList<StorableKey> SelectedKeys => browseAdapter.SelectedKeys;

	public string LocationText => browseAdapter.LocationText;

	public bool IsLoading => browseAdapter.IsLoading;

	public bool CanGoBack => browseAdapter.CanGoBack;

	public bool CanGoForward => browseAdapter.CanGoForward;

	public bool CanGoUp => browseAdapter.CanGoUp;

	public bool CanRefresh => !IsLoading;

	public string StatusText =>
		operationError
		?? browseAdapter.ErrorMessage
		?? browseAdapter.StatusText;

	public Task InitializeAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.InitializeAsync(cancellationToken);

	public Task NavigateToPathAsync(
		string path,
		CancellationToken cancellationToken = default) =>
		browseAdapter.NavigateToPathAsync(path, cancellationToken);

	public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.NavigateHomeAsync(cancellationToken);

	public Task NavigateToItemAsync(
		BrowseItemViewModel item,
		CancellationToken cancellationToken = default) =>
		browseAdapter.NavigateToItemAsync(item, cancellationToken);

	public Task GoBackAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.GoBackAsync(cancellationToken);

	public Task GoForwardAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.GoForwardAsync(cancellationToken);

	public Task GoUpAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.GoUpAsync(cancellationToken);

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		browseAdapter.RefreshAsync(cancellationToken);

	public void UpdateViewport(BrowseViewport viewport) =>
		browseAdapter.UpdateViewport(viewport);

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems) =>
		browseAdapter.SetSelection(selectedItems);

	public void SetViewMode(FolderViewMode mode)
	{
		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		ViewMode = mode;
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void ReportOperationCanceled()
	{
		operationError = Strings.OperationCanceled.GetLocalized();
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		browseAdapter.Updated -= BrowseAdapter_Updated;
		browseAdapter.Dispose();
	}

	private void BrowseAdapter_Updated(
		object? sender,
		CoreBrowseUpdatedEventArgs args)
	{
		isApplyingUpdate = true;
		try
		{
			foreach (var change in args.ItemChanges)
			{
				switch (change)
				{
					case BrowseItemViewModelAdded added:
						Items.Insert(added.Index, added.Item);
						break;
					case BrowseItemViewModelRemoved removed:
						Items.RemoveAt(removed.Index);
						break;
					case BrowseItemViewModelReplaced replaced:
						Items[replaced.Index] = replaced.Item;
						break;
					case BrowseItemViewModelMoved moved:
						Items.Move(moved.PreviousIndex, moved.CurrentIndex);
						break;
					case BrowseItemViewModelsReset reset:
						Items.Clear();
						foreach (var item in reset.Items)
						{
							Items.Add(item);
						}

						break;
					default:
						throw new InvalidOperationException(
							$"Unsupported browse item change '{change.GetType().Name}'.");
				}
			}

			operationError = null;
			OnPropertyChanged(nameof(SelectedKeys));
			OnPropertyChanged(nameof(LocationText));
			OnPropertyChanged(nameof(IsLoading));
			OnPropertyChanged(nameof(CanGoBack));
			OnPropertyChanged(nameof(CanGoForward));
			OnPropertyChanged(nameof(CanGoUp));
			OnPropertyChanged(nameof(CanRefresh));
			OnPropertyChanged(nameof(StatusText));
		}
		finally
		{
			isApplyingUpdate = false;
		}
	}
}

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
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.ViewModels;

public enum FolderViewMode
{
	Details,
	Grid,
	List,
}

public sealed class FolderBrowserViewModel : ObservableObject, IDisposable
{
	private readonly CoreBrowseAdapter _browseAdapter;

	private readonly IUIDispatcher _dispatcher;

	private string? _operationError;

	private bool _isApplyingUpdate;

	private int _isDisposed;

	private FolderViewMode _viewMode = FolderViewMode.Details;

	internal WindowCommandManager CommandManager { get; }

	public ObservableCollection<BrowseItemViewModel> Items { get; } = [];

	public FolderViewMode ViewMode
	{
		get => _viewMode;
		private set => SetProperty(ref _viewMode, value);
	}

	public bool IsApplyingUpdate => _isApplyingUpdate;

	public IReadOnlyList<StorableKey> SelectedKeys => _browseAdapter.SelectedKeys;

	public string LocationText => _browseAdapter.LocationText;

	public bool IsLoading => _browseAdapter.IsLoading;

	public bool CanGoBack => _browseAdapter.CanGoBack;

	public bool CanGoForward => _browseAdapter.CanGoForward;

	public bool CanGoUp => _browseAdapter.CanGoUp;

	public bool CanRefresh => !IsLoading;

	public string StatusText =>
		_operationError
		?? _browseAdapter.ErrorMessage
		?? _browseAdapter.StatusText;

	public FolderBrowserViewModel(PaneModel pane, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(commandManager);
		ArgumentNullException.ThrowIfNull(dispatcher);

		CommandManager = commandManager;
		_dispatcher = dispatcher;
		_browseAdapter = new CoreBrowseAdapter(pane, dataRoot, dispatcher);
		_viewMode = ToFolderViewMode(_browseAdapter.LayoutMode);
		_browseAdapter.Updated += BrowseAdapter_Updated;
	}

	public Task InitializeAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.InitializeAsync(cancellationToken);

	public Task NavigateToPathAsync(string path, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToPathAsync(path, cancellationToken);

	public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateHomeAsync(cancellationToken);

	public Task NavigateToItemAsync(BrowseItemViewModel item, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToItemAsync(item, cancellationToken);

	public Task NavigateToReferenceAsync(StorableReference reference, CancellationToken cancellationToken = default) =>
		_browseAdapter.NavigateToReferenceAsync(reference, cancellationToken);

	public Task GoBackAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoBackAsync(cancellationToken);

	public Task GoForwardAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoForwardAsync(cancellationToken);

	public Task GoUpAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.GoUpAsync(cancellationToken);

	public Task RefreshAsync(CancellationToken cancellationToken = default) =>
		_browseAdapter.RefreshAsync(cancellationToken);

	public void UpdateViewport(BrowseViewport viewport) =>
		_browseAdapter.UpdateViewport(viewport);

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems) =>
		_browseAdapter.SetSelection(selectedItems);

	public async Task SetViewModeAsync(FolderViewMode mode, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		await SetViewModeOnUiAsync(mode).ConfigureAwait(false);
		try
		{
			await _browseAdapter.UpdateLayoutModeAsync(ToViewLayoutMode(mode), cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			await SetViewModeOnUiAsync(ToFolderViewMode(_browseAdapter.LayoutMode)).ConfigureAwait(false);
			throw;
		}
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void ReportOperationCanceled()
	{
		_operationError = Strings.OperationCanceled.GetLocalized();
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_browseAdapter.Updated -= BrowseAdapter_Updated;
		_browseAdapter.Dispose();
	}

	private void BrowseAdapter_Updated(object? sender, CoreBrowseUpdatedEventArgs args)
	{
		_isApplyingUpdate = true;
		try
		{
			ViewMode = ToFolderViewMode(_browseAdapter.LayoutMode);

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
						throw new InvalidOperationException($"Unsupported browse item change '{change.GetType().Name}'.");
				}
			}

			_operationError = null;
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
			_isApplyingUpdate = false;
		}
	}

	private Task SetViewModeOnUiAsync(FolderViewMode mode)
	{
		if (_dispatcher.HasThreadAccess)
		{
			ViewMode = mode;

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			try
			{
				ViewMode = mode;
				completion.SetResult(true);
			}
			catch (Exception exception)
			{
				completion.SetException(exception);
			}
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected a folder view update."));
		}

		return completion.Task;
	}

	private static FolderViewMode ToFolderViewMode(ViewLayoutMode mode) =>
		mode switch
		{
			ViewLayoutMode.Details => FolderViewMode.Details,
			ViewLayoutMode.List => FolderViewMode.List,
			ViewLayoutMode.Grid => FolderViewMode.Grid,
			ViewLayoutMode.Columns => FolderViewMode.Details,
			_ => throw new InvalidOperationException($"Unsupported folder layout mode '{mode}'."),
		};

	private static ViewLayoutMode ToViewLayoutMode(FolderViewMode mode) =>
		mode switch
		{
			FolderViewMode.Details => ViewLayoutMode.Details,
			FolderViewMode.List => ViewLayoutMode.List,
			FolderViewMode.Grid => ViewLayoutMode.Grid,
			_ => throw new InvalidOperationException($"Unsupported folder view mode '{mode}'."),
		};

	private void EnsureActive()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);
	}
}

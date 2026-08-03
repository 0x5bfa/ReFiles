// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Commands;
using Files.Infrastructure;
using Files.Localization;
using Files.Core.AppModels;
using Files.Core.Data;

namespace Files.ViewModels;

public sealed class TabViewModel : ObservableObject, IDisposable
{
	private readonly TabModel _tab;

	private readonly IFilesDataRoot _dataRoot;

	private readonly IUIDispatcher _dispatcher;

	private readonly WindowCommandManager _commandManager;

	private readonly Dictionary<Guid, PaneViewModel> _paneViewModels = [];

	private int _isDisposed;

	private int _refreshQueued;

	private string? _operationError;

	private bool _isRefreshing;

	public Guid Id => _tab.Id;

	public ObservableCollection<PaneViewModel> Panes { get; }

	public PaneViewModel? ActivePane => Panes.FirstOrDefault(static pane => pane.IsActive);

	public PaneSplitOrientation SplitOrientation => _tab.SplitOrientation;

	public string Title => ActivePane?.Title ?? Strings.NewTab.GetLocalized();

	public string StatusText => _operationError ?? ActivePane?.FolderBrowser.StatusText ?? Strings.NoPane.GetLocalized();

	public bool CanClosePane => Panes.Count > 1;

	public TabViewModel(TabModel tab, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(tab);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandManager);

		_tab = tab;
		_dataRoot = dataRoot;
		_dispatcher = dispatcher;
		_commandManager = commandManager;
		Panes = [];

		tab.StateChanged += Tab_StateChanged;
		RefreshFromCore();
	}

	public async Task OpenPaneAsync(PaneSplitOrientation orientation, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		await _tab.OpenSplitAsync(orientation, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	public async Task CloseActivePaneAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (ActivePane is not { } activePane)
		{
			return;
		}

		await _tab.ClosePaneAsync(activePane.Id, cancellationToken).ConfigureAwait(false);
	}

	public bool SetActivePane(Guid paneId)
	{
		EnsureActive();

		return _tab.SetActivePane(paneId);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		_operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_tab.StateChanged -= Tab_StateChanged;

		foreach (var pane in _paneViewModels.Values)
		{
			pane.PropertyChanged -= PaneViewModel_PropertyChanged;
			pane.Dispose();
		}

		_paneViewModels.Clear();
		Panes.Clear();
	}

	private void Tab_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref _refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!_dispatcher.TryEnqueue(() =>
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
			RefreshFromCore();
		}))
		{
			Interlocked.Exchange(ref _refreshQueued, 0);
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a tab update.");
			}
		}
	}

	private void RefreshFromCore()
	{
		if (Volatile.Read(ref _isDisposed) is not 0 || _isRefreshing)
		{
			return;
		}

		_isRefreshing = true;

		try
		{
			var corePanes = _tab.Panes;
			var corePaneIds = corePanes.Select(static pane => pane.Id).ToHashSet();

			foreach (var removedId in _paneViewModels.Keys .Where(id => !corePaneIds.Contains(id)) .ToArray())
			{
				var removedPane = _paneViewModels[removedId];
				removedPane.PropertyChanged -= PaneViewModel_PropertyChanged;
				removedPane.Dispose();
				_paneViewModels.Remove(removedId);
			}

			foreach (var corePane in corePanes)
			{
				if (!_paneViewModels.ContainsKey(corePane.Id))
				{
					var paneViewModel = new PaneViewModel(corePane, _dataRoot, _dispatcher, _commandManager);
					paneViewModel.PropertyChanged += PaneViewModel_PropertyChanged;
					_paneViewModels[corePane.Id] = paneViewModel;
				}
			}

			var orderedPanes = corePanes.Select(corePane => _paneViewModels[corePane.Id]).ToArray();
			foreach (var pane in orderedPanes)
			{
				pane.SetActive(_tab.ActivePane?.Id == pane.Id);
			}

			ObservableCollectionSynchronizer.Synchronize(Panes, orderedPanes);

			_operationError = null;
			OnPropertyChanged(nameof(ActivePane));
			OnPropertyChanged(nameof(SplitOrientation));
			OnPropertyChanged(nameof(Title));
			OnPropertyChanged(nameof(StatusText));
			OnPropertyChanged(nameof(CanClosePane));
		}
		finally
		{
			_isRefreshing = false;
		}
	}

	private void PaneViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(PaneViewModel.StatusText) or nameof(PaneViewModel.Title))
		{
			OnPropertyChanged(nameof(Title));
			OnPropertyChanged(nameof(StatusText));
		}
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

}

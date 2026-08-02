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
	private readonly TabModel tab;

	private readonly IFilesDataRoot dataRoot;

	private readonly IUIDispatcher dispatcher;

	private readonly WindowCommandManager commandManager;

	private readonly Dictionary<Guid, PaneViewModel> paneViewModels = [];

	private int isDisposed;

	private int refreshQueued;

	private string? operationError;

	private bool isRefreshing;

	public Guid Id => tab.Id;

	public ObservableCollection<PaneViewModel> Panes { get; }

	public PaneViewModel? ActivePane =>
		Panes.FirstOrDefault(static pane => pane.IsActive);

	public PaneSplitOrientation SplitOrientation => tab.SplitOrientation;

	public string Title => ActivePane?.Title ?? Strings.NewTab.GetLocalized();

	public string StatusText =>
		operationError
		?? ActivePane?.FolderBrowser.StatusText
		?? Strings.NoPane.GetLocalized();

	public bool CanClosePane => Panes.Count > 1;

	public TabViewModel(TabModel tab, IFilesDataRoot dataRoot, IUIDispatcher dispatcher, WindowCommandManager commandManager)
	{
		ArgumentNullException.ThrowIfNull(tab);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(commandManager);

		this.tab = tab;
		this.dataRoot = dataRoot;
		this.dispatcher = dispatcher;
		this.commandManager = commandManager;
		Panes = [];

		tab.StateChanged += Tab_StateChanged;
		RefreshFromCore();
	}

	public async Task OpenPaneAsync(PaneSplitOrientation orientation, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		await tab.OpenSplitAsync(orientation, cancellationToken: cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task CloseActivePaneAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		if (ActivePane is not { } activePane)
		{
			return;
		}

		await tab.ClosePaneAsync(activePane.Id, cancellationToken)
			.ConfigureAwait(false);
	}

	public bool SetActivePane(Guid paneId)
	{
		EnsureActive();

		return tab.SetActivePane(paneId);
	}

	public void ReportOperationError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		operationError = exception.Message;
		OnPropertyChanged(nameof(StatusText));
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		tab.StateChanged -= Tab_StateChanged;
		foreach (var pane in paneViewModels.Values)
		{
			pane.PropertyChanged -= PaneViewModel_PropertyChanged;
			pane.Dispose();
		}

		paneViewModels.Clear();
		Panes.Clear();
	}

	private void Tab_StateChanged(object? sender, EventArgs args)
	{
		if (Interlocked.Exchange(ref refreshQueued, 1) is not 0)
		{
			return;
		}

		if (!dispatcher.TryEnqueue(() => {Interlocked.Exchange(ref refreshQueued, 0); RefreshFromCore();}))
		{
			Interlocked.Exchange(ref refreshQueued, 0);
			if (Volatile.Read(ref isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a tab update.");
			}
		}
	}

	private void RefreshFromCore()
	{
		if (Volatile.Read(ref isDisposed) is not 0 || isRefreshing)
		{
			return;
		}

		isRefreshing = true;
		try
		{
			var corePanes = tab.Panes;
			var corePaneIds = corePanes
				.Select(static pane => pane.Id)
				.ToHashSet();

			foreach (var removedId in paneViewModels.Keys .Where(id => !corePaneIds.Contains(id)) .ToArray())
			{
				var removedPane = paneViewModels[removedId];
				removedPane.PropertyChanged -= PaneViewModel_PropertyChanged;
				removedPane.Dispose();
				paneViewModels.Remove(removedId);
			}

			foreach (var corePane in corePanes)
			{
				if (!paneViewModels.ContainsKey(corePane.Id))
				{
					var paneViewModel = new PaneViewModel(corePane, dataRoot, dispatcher, commandManager);
					paneViewModel.PropertyChanged += PaneViewModel_PropertyChanged;
					paneViewModels[corePane.Id] = paneViewModel;
				}
			}

			var orderedPanes = corePanes
				.Select(corePane => paneViewModels[corePane.Id])
				.ToArray();
			foreach (var pane in orderedPanes)
			{
				pane.SetActive(tab.ActivePane?.Id == pane.Id);
			}

			ObservableCollectionSynchronizer.Synchronize(Panes, orderedPanes);

			operationError = null;
			OnPropertyChanged(nameof(ActivePane));
			OnPropertyChanged(nameof(SplitOrientation));
			OnPropertyChanged(nameof(Title));
			OnPropertyChanged(nameof(StatusText));
			OnPropertyChanged(nameof(CanClosePane));
		}
		finally
		{
			isRefreshing = false;
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
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);

}

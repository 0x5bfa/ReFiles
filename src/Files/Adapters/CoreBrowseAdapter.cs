// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Files.Infrastructure;
using Files.Localization;
using Files.ViewModels;
using Files.Core.AppModels;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;

namespace Files.Adapters;

internal sealed class CoreBrowseAdapter : IDisposable
{
	private readonly PaneModel pane;
	private readonly IFilesDataRoot dataRoot;
	private readonly IUIDispatcher dispatcher;
	private readonly CancellationTokenSource lifetime = new();
	private readonly Lock pendingLock = new();
	private readonly Queue<PendingItemBatch> pendingItemBatches = new();
	private readonly Dictionary<StorableKey, ThumbnailResult?> pendingThumbnails = [];
	private readonly List<BrowseItemViewModel> items = [];
	private PendingState? pendingState;
	private IReadOnlyList<StorableKey>? pendingSelection;
	private long appliedItemsVersion = -1;
	private bool drainQueued;
	private int isDisposed;

	public CoreBrowseAdapter(PaneModel pane, IFilesDataRoot dataRoot, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(pane);
		ArgumentNullException.ThrowIfNull(dataRoot);
		ArgumentNullException.ThrowIfNull(dispatcher);

		this.pane = pane;
		this.dataRoot = dataRoot;
		this.dispatcher = dispatcher;

		SelectedKeys = Array.Empty<StorableKey>();
		pane.StateChanged += Pane_StateChanged;
		pane.BrowseSession.ItemsChanged += BrowseSession_ItemsChanged;
		pane.BrowseSession.ItemPresentationChanged +=
			BrowseSession_ItemPresentationChanged;
		pane.BrowseSession.SelectionChanged += BrowseSession_SelectionChanged;
		QueueInitialSnapshot();
	}

	public IReadOnlyList<StorableKey> SelectedKeys { get; private set; }

	public string LocationText { get; private set; } = Strings.Home.GetLocalized();

	public string? ErrorMessage { get; private set; }

	public bool IsLoading { get; private set; }

	public bool CanGoBack => pane.CanGoBack;

	public bool CanGoForward => pane.CanGoForward;

	public bool CanGoUp => pane.CanGoUp;

	public string StatusText =>
		ErrorMessage
		?? (IsLoading
			? Strings.Loading.GetLocalized()
			: string.Format(CultureInfo.CurrentCulture, items.Count is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized(), items.Count));

	public event EventHandler<CoreBrowseUpdatedEventArgs>? Updated;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.NavigateAsync(HomeLocation.Instance, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
	}

	public Task NavigateHomeAsync(CancellationToken cancellationToken = default) =>
		InitializeAsync(cancellationToken);

	public async Task NavigateToPathAsync(string path, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (string.Equals(path, Strings.Home.GetLocalized(), StringComparison.OrdinalIgnoreCase) || string.Equals(path, "Home", StringComparison.OrdinalIgnoreCase))
		{
			await InitializeAsync(cancellationToken).ConfigureAwait(false);

			return;
		}

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		var model = await dataRoot.ResolveAsync(new StorageAddress("file", path), linkedCancellation.Token).ConfigureAwait(false);
		try
		{
			if (model is not IFolderModel)
			{
				throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.NotFolderFormat.GetLocalized(), path));
			}

			await pane.NavigateAsync(new FolderLocation(model.Reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
		}
		finally
		{
			await model.DisposeAsync().ConfigureAwait(false);
		}
	}

	public async Task NavigateToItemAsync(BrowseItemViewModel item, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(item);

		if (!item.IsFolder)
		{
			return;
		}

		await NavigateToReferenceAsync(item.Reference, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task NavigateToReferenceAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(reference);

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.NavigateAsync(new FolderLocation(reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoBackAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.GoBackAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoForwardAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.GoForwardAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoUpAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.GoUpAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();
		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await pane.RefreshAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public void UpdateViewport(BrowseViewport viewport)
	{
		EnsureActive();
		pane.UpdateViewport(viewport);
	}

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(selectedItems);

		var selectedKeys = selectedItems
			.Select(static item => item.Reference.GetKey())
			.ToArray();
		var focusedKey = selectedKeys.FirstOrDefault();
		pane.BrowseSession.SetSelection(selectedKeys, selectedKeys.Length is 0 ? null : focusedKey, selectedKeys.Length is 0 ? null : focusedKey);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
		{
			return;
		}

		pane.StateChanged -= Pane_StateChanged;
		pane.BrowseSession.ItemsChanged -= BrowseSession_ItemsChanged;
		pane.BrowseSession.ItemPresentationChanged -=
			BrowseSession_ItemPresentationChanged;
		pane.BrowseSession.SelectionChanged -= BrowseSession_SelectionChanged;
		lifetime.Cancel();
		lifetime.Dispose();
		lock (pendingLock)
		{
			pendingItemBatches.Clear();
			pendingThumbnails.Clear();
			pendingState = null;
			pendingSelection = null;
		}

		Updated = null;
	}

	private void Pane_StateChanged(object? sender, EventArgs args)
	{
		var session = pane.BrowseSession;
		lock (pendingLock)
		{
			pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
		}

		ScheduleDrain();
	}

	private void BrowseSession_ItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		var changes = args.Changes.Select(ProjectChange).ToArray();
		lock (pendingLock)
		{
			pendingItemBatches.Enqueue(new PendingItemBatch(args.PreviousVersion, args.Version, changes));
		}

		ScheduleDrain();
	}

	private void BrowseSession_SelectionChanged(object? sender, EventArgs args)
	{
		var selection = pane.BrowseSession.Selection.SelectedKeys.ToArray();
		lock (pendingLock)
		{
			pendingSelection = selection;
		}

		ScheduleDrain();
	}

	private void BrowseSession_ItemPresentationChanged(object? sender, BrowseItemPresentationChangedEventArgs args)
	{
		lock (pendingLock)
		{
			pendingThumbnails[args.Key] = args.Presentation.Thumbnail;
		}

		ScheduleDrain();
	}

	private void QueueInitialSnapshot()
	{
		var session = pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		lock (pendingLock)
		{
			pendingItemBatches.Enqueue(new PendingItemBatch(-1, session.ItemsVersion, [reset]));
			pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
			pendingSelection = session.Selection.SelectedKeys.ToArray();
			foreach (var item in session.Items)
			{
				var key = item.Reference.GetKey();
				if (session.TryGetPresentation(key, out var presentation))
				{
					pendingThumbnails[key] = presentation.Thumbnail;
				}
			}
		}

		ScheduleDrain();
	}

	private void ScheduleDrain()
	{
		lock (pendingLock)
		{
			if (drainQueued || Volatile.Read(ref isDisposed) is not 0)
			{
				return;
			}

			drainQueued = true;
		}

		if (!dispatcher.TryEnqueue(DrainPendingUpdates))
		{
			lock (pendingLock)
			{
				drainQueued = false;
			}

			if (Volatile.Read(ref isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a Core update.");
			}
		}
	}

	private void DrainPendingUpdates()
	{
		PendingItemBatch[] itemBatches;
		PendingState? state;
		IReadOnlyList<StorableKey>? selection;
		KeyValuePair<StorableKey, ThumbnailResult?>[] thumbnails;
		lock (pendingLock)
		{
			if (Volatile.Read(ref isDisposed) is not 0)
			{
				return;
			}

			itemBatches = pendingItemBatches
				.OrderBy(static batch => batch.Version)
				.ToArray();
			pendingItemBatches.Clear();
			state = pendingState;
			pendingState = null;
			selection = pendingSelection;
			pendingSelection = null;
			thumbnails = pendingThumbnails.ToArray();
			pendingThumbnails.Clear();
			drainQueued = false;
		}

		var appliedChanges = new List<BrowseItemViewModelChange>();
		foreach (var batch in itemBatches)
		{
			if (batch.Version <= appliedItemsVersion)
			{
				continue;
			}

			if (appliedItemsVersion >= 0 && batch.PreviousVersion != appliedItemsVersion)
			{
				ResetFromCurrentSession(appliedChanges);
				break;
			}

			if (!TryApplyChanges(batch.Changes, appliedChanges))
			{
				ResetFromCurrentSession(appliedChanges);
				break;
			}

			appliedItemsVersion = batch.Version;
		}

		if (state is not null)
		{
			IsLoading = state.IsLoading;
			ErrorMessage = state.ErrorMessage;
			LocationText = state.LocationText;
		}

		if (selection is not null)
		{
			SelectedKeys = selection;
		}

		if (appliedChanges.Count > 0 || state is not null || selection is not null)
		{
			Updated?.Invoke(this, new CoreBrowseUpdatedEventArgs(appliedChanges));
		}

		foreach (var thumbnail in thumbnails)
		{
			_ = ApplyThumbnailAsync(thumbnail.Key, thumbnail.Value);
		}
	}

	private async Task ApplyThumbnailAsync(StorableKey key, ThumbnailResult? thumbnail)
	{
		try
		{
			var image = thumbnail is null
				? null
				: await ThumbnailImageFactory
					.CreateAsync(thumbnail.Content)
					.ConfigureAwait(true);
			if (Volatile.Read(ref isDisposed) is not 0)
			{
				return;
			}

			items.FirstOrDefault(item => item.Reference.GetKey() == key)
				?.SetThumbnail(image);
		}
		catch
		{
			// Thumbnail decoding is best effort.
		}
	}

	private bool TryApplyChanges(IReadOnlyList<BrowseItemViewModelChange> changes, ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		foreach (var change in changes)
		{
			switch (change)
			{
				case BrowseItemViewModelAdded added
					when added.Index >= 0 && added.Index <= items.Count:
					items.Insert(added.Index, added.Item);
					break;
				case BrowseItemViewModelRemoved removed
					when removed.Index >= 0 && removed.Index < items.Count:
					items.RemoveAt(removed.Index);
					break;
				case BrowseItemViewModelReplaced replaced
					when replaced.Index >= 0 && replaced.Index < items.Count:
					items[replaced.Index] = replaced.Item;
					break;
				case BrowseItemViewModelMoved moved
					when moved.PreviousIndex >= 0
						&& moved.PreviousIndex < items.Count
						&& moved.CurrentIndex >= 0
						&& moved.CurrentIndex < items.Count:
					var item = items[moved.PreviousIndex];
					items.RemoveAt(moved.PreviousIndex);
					items.Insert(moved.CurrentIndex, item);
					break;
				case BrowseItemViewModelsReset reset:
					items.Clear();
					items.AddRange(reset.Items);
					break;
				default:

					return false;
			}

			appliedChanges.Add(change);
		}

		return true;
	}

	private void ResetFromCurrentSession(ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		var session = pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		items.Clear();
		items.AddRange(reset.Items);
		appliedItemsVersion = session.ItemsVersion;
		appliedChanges.Clear();
		appliedChanges.Add(reset);
	}

	private static BrowseItemViewModelChange ProjectChange(BrowseItemChange change) =>
		change switch
		{
			BrowseItemAdded added => new BrowseItemViewModelAdded(added.Index, CreateItemViewModel(added.Item)),
			BrowseItemRemoved removed => new BrowseItemViewModelRemoved(removed.Index),
			BrowseItemReplaced replaced => new BrowseItemViewModelReplaced(replaced.Index, CreateItemViewModel(replaced.NewItem)),
			BrowseItemMoved moved => new BrowseItemViewModelMoved(moved.PreviousIndex, moved.CurrentIndex),
			BrowseItemsReset reset => new BrowseItemViewModelsReset(reset.Items.Select(CreateItemViewModel).ToArray()),
			_ => throw new InvalidOperationException($"Unsupported Core browse item change '{change.GetType().Name}'."),
		};

	private static BrowseItemViewModel CreateItemViewModel(IStorableModel item) =>
		new(item.Name, item is IFolderModel, item.Reference);

	private CancellationTokenSource CreateLinkedCancellation(CancellationToken cancellationToken) =>
		CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);

	private static string GetLocationText(BrowseLocation? location)
	{
		return location switch
		{
			HomeLocation => Strings.Home.GetLocalized(),
			FolderLocation folder when folder.Folder.LastKnownAddress is
				{ Scheme: var scheme, Value: var value }
				&& string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase)
				=> value,
			FolderLocation folder => folder.Folder.LastKnownAddress?.ToString()
				?? folder.Folder.ItemId,
			_ => location?.GetType().Name ?? Strings.Home.GetLocalized(),
		};
	}

	private void EnsureActive() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);

	private sealed record PendingItemBatch(long PreviousVersion, long Version, IReadOnlyList<BrowseItemViewModelChange> Changes);

	private sealed record PendingState(bool IsLoading, string? ErrorMessage, string LocationText);
}

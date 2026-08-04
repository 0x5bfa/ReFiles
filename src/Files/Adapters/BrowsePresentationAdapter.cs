// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using Files.Infrastructure;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Sessions;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Adapters;

internal sealed class BrowsePresentationAdapter : IDisposable, IAsyncDisposable
{
	private const int MaxItemsPerDrain = 128;

	private readonly BrowsePaneSession _pane;
	private readonly IStorageWorkspace _workspace;
	private readonly IUIDispatcher _dispatcher;
	private readonly IBrowsePrefetchCoordinator _prefetch;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Lock _pendingLock = new();
	private readonly Queue<PendingItemBatch> _pendingItemBatches = new();
	private readonly Dictionary<StorableKey, ThumbnailResult?> _pendingThumbnails = [];
	private readonly List<BrowseItemViewModel> _items = [];
	private PendingState? _pendingState;
	private IReadOnlyList<StorableKey>? _pendingSelection;
	private long _appliedItemsVersion = -1;
	private bool _drainQueued;
	private int _isDisposed;

	public BrowsePresentationAdapter(BrowsePaneSession pane, IStorageWorkspace workspace, IUIDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(pane);
		ArgumentNullException.ThrowIfNull(workspace);
		ArgumentNullException.ThrowIfNull(dispatcher);

		_pane = pane;
		_workspace = workspace;
		_dispatcher = dispatcher;
		_prefetch = new BrowsePrefetchCoordinator(_pane.BrowseSession);

		SelectedKeys = Array.Empty<StorableKey>();
		_pane.NavigationStateChanged += Pane_StateChanged;
		_pane.BrowseSession.ItemsChanged += BrowseSession_ItemsChanged;
		_pane.BrowseSession.ItemPresentationChanged +=
			BrowseSession_ItemPresentationChanged;
		_pane.BrowseSession.SelectionChanged += BrowseSession_SelectionChanged;
		QueueInitialSnapshot();
	}

	public IReadOnlyList<StorableKey> SelectedKeys { get; private set; }

	public string LocationText { get; private set; } = Strings.Home.GetLocalized();

	public string? ErrorMessage { get; private set; }

	public bool IsLoading { get; private set; }

	public bool CanGoBack => _pane.CanGoBack;

	public bool CanGoForward => _pane.CanGoForward;

	public bool CanGoUp => _pane.CanGoUp;

	public ViewLayoutMode LayoutMode => _pane.BrowseSession.ViewSettings.LayoutMode;

	public string StatusText =>
		ErrorMessage
		?? (IsLoading
			? Strings.Loading.GetLocalized()
			: string.Format(CultureInfo.CurrentCulture, _items.Count is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized(), _items.Count));

	public event EventHandler<CoreBrowseUpdatedEventArgs>? Updated;

	public IReadOnlyList<BrowseItemViewModel> Items => _items;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.NavigateAsync(HomeLocation.Instance, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
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
		var model = await _workspace.ResolveAsync(new StorageAddress("file", path), linkedCancellation.Token).ConfigureAwait(false);
		try
		{
			if (model is not IFolderModel)
			{
				throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.NotFolderFormat.GetLocalized(), path));
			}

			await _pane.NavigateAsync(new FolderLocation(model.Reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
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
		await _pane.NavigateAsync(new FolderLocation(reference), cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoBackAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.GoBackAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoForwardAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.GoForwardAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task GoUpAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.GoUpAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.RefreshAsync(linkedCancellation.Token).ConfigureAwait(false);
	}

	public void UpdateViewport(BrowseViewport viewport)
	{
		EnsureActive();

		_prefetch.UpdateViewport(viewport, _pane.BrowseSession.ViewSettings, _pane.BrowseSession.Generation);
	}

	public async ValueTask UpdateLayoutModeAsync(ViewLayoutMode mode, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		if (!Enum.IsDefined(mode))
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		var currentSettings = _pane.BrowseSession.ViewSettings;
		if (currentSettings.LayoutMode == mode)
		{
			return;
		}

		var settings = new BrowseViewSettings(
			mode,
			currentSettings.Columns,
			currentSettings.SortPropertyId,
			currentSettings.SortDirection,
			currentSettings.ItemSize);

		using var linkedCancellation = CreateLinkedCancellation(cancellationToken);
		await _pane.BrowseSession.UpdateViewSettingsAsync(settings, linkedCancellation.Token).ConfigureAwait(false);
	}

	public void SetSelection(IEnumerable<BrowseItemViewModel> selectedItems)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(selectedItems);

		var selectedKeys = selectedItems
			.Select(static item => item.Reference.GetKey())
			.ToArray();
		var focusedKey = selectedKeys.FirstOrDefault();
		_pane.BrowseSession.SetSelection(selectedKeys, selectedKeys.Length is 0 ? null : focusedKey, selectedKeys.Length is 0 ? null : focusedKey);
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		Exception? prefetchError = null;
		try
		{
			await _prefetch.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception error)
		{
			prefetchError = error;
		}

		_pane.NavigationStateChanged -= Pane_StateChanged;
		_pane.BrowseSession.ItemsChanged -= BrowseSession_ItemsChanged;
		_pane.BrowseSession.ItemPresentationChanged -=
			BrowseSession_ItemPresentationChanged;
		_pane.BrowseSession.SelectionChanged -= BrowseSession_SelectionChanged;
		_lifetime.Cancel();
		_lifetime.Dispose();
		lock (_pendingLock)
		{
			_pendingItemBatches.Clear();
			_pendingThumbnails.Clear();
			_pendingState = null;
			_pendingSelection = null;
		}

		Updated = null;
		if (prefetchError is not null)
		{
			throw prefetchError;
		}
	}

	private void Pane_StateChanged(object? sender, EventArgs args)
	{
		var session = _pane.BrowseSession;
		lock (_pendingLock)
		{
			_pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
		}

		ScheduleDrain();
	}

	private void BrowseSession_ItemsChanged(object? sender, BrowseItemsChangedEventArgs args)
	{
		var changes = args.Changes.Select(ProjectChange).ToArray();
		lock (_pendingLock)
		{
			_pendingItemBatches.Enqueue(new PendingItemBatch(args.PreviousVersion, args.Version, changes));
		}

		ScheduleDrain();
	}

	private void BrowseSession_SelectionChanged(object? sender, EventArgs args)
	{
		var selection = _pane.BrowseSession.Selection.SelectedKeys.ToArray();
		lock (_pendingLock)
		{
			_pendingSelection = selection;
		}

		ScheduleDrain();
	}

	private void BrowseSession_ItemPresentationChanged(object? sender, BrowseItemPresentationChangedEventArgs args)
	{
		lock (_pendingLock)
		{
			_pendingThumbnails[args.Key] = args.Presentation.Thumbnail;
		}

		ScheduleDrain();
	}

	private void QueueInitialSnapshot()
	{
		var session = _pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		lock (_pendingLock)
		{
			_pendingItemBatches.Enqueue(new PendingItemBatch(-1, session.ItemsVersion, [reset]));
			_pendingState = new PendingState(session.IsLoading, session.Error?.Message, GetLocationText(session.Location));
			_pendingSelection = session.Selection.SelectedKeys.ToArray();
			foreach (var item in session.Items)
			{
				var key = item.Reference.GetKey();
				if (session.TryGetPresentation(key, out var presentation))
				{
					_pendingThumbnails[key] = presentation.Thumbnail;
				}
			}
		}

		ScheduleDrain();
	}

	private void ScheduleDrain()
	{
		lock (_pendingLock)
		{
			if (_drainQueued || Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			_drainQueued = true;
		}

		if (!_dispatcher.TryEnqueue(DrainPendingUpdates))
		{
			lock (_pendingLock)
			{
				_drainQueued = false;
			}

			if (Volatile.Read(ref _isDisposed) is 0)
			{
				throw new InvalidOperationException("The Files UI dispatcher rejected a Core update.");
			}
		}
	}

	private void DrainPendingUpdates()
	{
		PendingItemBatch[] itemBatches;
		bool hasPendingItemBatches;
		PendingState? state;
		IReadOnlyList<StorableKey>? selection;
		KeyValuePair<StorableKey, ThumbnailResult?>[] thumbnails;
		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			itemBatches = TakePendingItemBatchesLocked(out hasPendingItemBatches);
			state = _pendingState;
			_pendingState = null;
			selection = _pendingSelection;
			_pendingSelection = null;
			thumbnails = _pendingThumbnails.ToArray();
			_pendingThumbnails.Clear();
			_drainQueued = false;
		}
		var appliedChanges = new List<BrowseItemViewModelChange>();
		foreach (var batch in itemBatches)
		{
			if (batch.Version <= _appliedItemsVersion)
			{
				continue;
			}

			if (_appliedItemsVersion >= 0 && batch.PreviousVersion != _appliedItemsVersion)
			{
				ResetFromCurrentSession(appliedChanges);
				break;
			}

			if (!TryApplyChanges(batch.Changes, appliedChanges))
			{
				ResetFromCurrentSession(appliedChanges);
				break;
			}

			_appliedItemsVersion = batch.Version;
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

		if (hasPendingItemBatches)
		{
			ScheduleDrain();
		}
	}

	private PendingItemBatch[] TakePendingItemBatchesLocked(out bool hasPendingItemBatches)
	{
		var batches = new List<PendingItemBatch>();
		var itemCount = 0;
		while (_pendingItemBatches.Count is not 0)
		{
			var batch = _pendingItemBatches.Peek();
			var batchItemCount = GetItemCount(batch.Changes);
			if (batches.Count is not 0 && itemCount + batchItemCount > MaxItemsPerDrain)
			{
				break;
			}

			_pendingItemBatches.Dequeue();
			batches.Add(batch);
			itemCount += batchItemCount;
		}

		hasPendingItemBatches = _pendingItemBatches.Count is not 0;

		return batches.ToArray();
	}

	private static int GetItemCount(IReadOnlyList<BrowseItemViewModelChange> changes)
	{
		var count = 0;
		foreach (var change in changes)
		{
			count += change switch
			{
				BrowseItemViewModelsReset reset => reset.Items.Count,
				_ => 1,
			};
		}

		return count;
	}

	private async Task ApplyThumbnailAsync(StorableKey key, ThumbnailResult? thumbnail)
	{
		try
		{
			var item = _items.FirstOrDefault(item => item.Reference.GetKey() == key);
			if (item is null)
			{
				RequeueThumbnailIfCurrent(key, thumbnail);

				return;
			}

			var image = thumbnail is null
				? null
				: await ThumbnailImageFactory
					.CreateAsync(thumbnail.Content)
					.ConfigureAwait(true);
			if (Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			item = _items.FirstOrDefault(item => item.Reference.GetKey() == key);
			if (item is not null)
			{
				item.SetThumbnail(image);

				return;
			}

			RequeueThumbnailIfCurrent(key, thumbnail);
		}
		catch
		{
			// Thumbnail decoding is best effort.
		}
	}

	private void RequeueThumbnailIfCurrent(StorableKey key, ThumbnailResult? thumbnail)
	{
		if (!_pane.BrowseSession.Items.Any(item => item.Reference.GetKey() == key))
		{
			return;
		}

		lock (_pendingLock)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_pendingThumbnails[key] = thumbnail;
			}
		}

		ScheduleDrain();
	}

	private bool TryApplyChanges(IReadOnlyList<BrowseItemViewModelChange> changes, ICollection<BrowseItemViewModelChange> appliedChanges)
	{
		foreach (var change in changes)
		{
			switch (change)
			{
				case BrowseItemViewModelAdded added
					when added.Index >= 0 && added.Index <= _items.Count:
					_items.Insert(added.Index, added.Item);
					break;
				case BrowseItemViewModelRemoved removed
					when removed.Index >= 0 && removed.Index < _items.Count:
					_items.RemoveAt(removed.Index);
					break;
				case BrowseItemViewModelReplaced replaced
					when replaced.Index >= 0 && replaced.Index < _items.Count:
					_items[replaced.Index] = replaced.Item;
					break;
				case BrowseItemViewModelMoved moved
					when moved.PreviousIndex >= 0
						&& moved.PreviousIndex < _items.Count
						&& moved.CurrentIndex >= 0
						&& moved.CurrentIndex < _items.Count:
					var item = _items[moved.PreviousIndex];
					_items.RemoveAt(moved.PreviousIndex);
					_items.Insert(moved.CurrentIndex, item);
					break;
				case BrowseItemViewModelsReset reset:
					_items.Clear();
					_items.AddRange(reset.Items);
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
		var session = _pane.BrowseSession;
		var reset = new BrowseItemViewModelsReset(session.Items.Select(CreateItemViewModel).ToArray());
		_items.Clear();
		_items.AddRange(reset.Items);
		_appliedItemsVersion = session.ItemsVersion;
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
		CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

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
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) is not 0, this);

	private sealed record PendingItemBatch(long PreviousVersion, long Version, IReadOnlyList<BrowseItemViewModelChange> Changes);

	private sealed record PendingState(bool IsLoading, string? ErrorMessage, string LocationText);
}

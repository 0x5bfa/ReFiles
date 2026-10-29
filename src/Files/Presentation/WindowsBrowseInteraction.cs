// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Files.Adapters;
using Files.Commands;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Sessions;
using Files.Core.Storage;
using Files.Core.Storage.Windows;
using Files.Infrastructure;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Presentation;

internal sealed class WindowsBrowseInteraction : IBrowseInteraction
{
	private readonly BrowsePaneSession _pane;
	private readonly BrowsePresentationAdapter _browseAdapter;
	private readonly WindowsStorageSource? _windowsSource;
	private readonly WindowsShellPropertiesService? _shellProperties;
	private readonly WindowsShellContextualCommandService? _shellContextualCommandService;
	private readonly IWindowsShellOperationService? _shellOperations;
	private readonly IUIDispatcher _dispatcher;
	private readonly ILogger<WindowsBrowseInteraction> _logger;
	private readonly nint _ownerWindowHandle;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly HashSet<string> _pendingContextualCommandIds = new(StringComparer.OrdinalIgnoreCase);
	private CancellationTokenSource? _contextualCommandRefreshCancellation;
	private Task _contextualCommandRefreshTask = Task.CompletedTask;
	private StorableReference? _contextualCommandRefreshLocation;
	private IReadOnlyList<StorableReference> _contextualCommandRefreshSelection = [];
	private IReadOnlyDictionary<string, WindowsShellContextualCommand> _contextualCommands = new Dictionary<string, WindowsShellContextualCommand>(StringComparer.OrdinalIgnoreCase);
	private int _isDisposed;

	private WindowCommandManager CommandManager { get; }
	private BrowseLocation? Location => _pane.Location;
	private bool IsLoading => _browseAdapter.IsLoading;
	private bool IsBusy => _browseAdapter.IsBusy;
	public bool CanCopy => !IsLoading && !IsBusy && GetSelectedShellClipboardItems().Count is not 0;
	public bool CanPaste => !IsLoading && !IsBusy && _windowsSource is not null && TryGetCurrentShellFolder(out _) && HasClipboardStorageItems();
	public bool CanShowNew => !IsLoading && !IsBusy && _windowsSource is not null && TryGetCurrentFileSystemFolder(out _);
	public bool CanShowShellContextMenu => _ownerWindowHandle is not 0 && _windowsSource is not null;

	internal WindowsBrowseInteraction(BrowsePaneSession pane, BrowsePresentationAdapter adapter, IStorageWorkspace workspace, IStorageOperationService operations,
		IUIDispatcher dispatcher, WindowCommandManager commandManager, nint ownerWindowHandle, ILogger<WindowsBrowseInteraction>? logger = null)
	{
		_pane = pane;
		_browseAdapter = adapter;
		_dispatcher = dispatcher;
		_logger = logger ?? NullLogger<WindowsBrowseInteraction>.Instance;
		CommandManager = commandManager;
		_ownerWindowHandle = ownerWindowHandle;
		_windowsSource = workspace.Sources.OfType<WindowsStorageSource>().FirstOrDefault();
		_shellProperties = _windowsSource is { } source ? new WindowsShellPropertiesService(source) : null;
		_shellContextualCommandService = _windowsSource is { } contextualSource && ownerWindowHandle != 0 ? new WindowsShellContextualCommandService(contextualSource) : null;
		_shellOperations = operations as IWindowsShellOperationService;
	}

	public Task<bool> IsShellDesktopAsync(StorableReference reference, CancellationToken cancellationToken)
	{
		return _windowsSource?.IsShellDesktopAsync(reference, cancellationToken).AsTask() ?? Task.FromResult(false);
	}

	public void Initialize()
	{
		QueueContextualCommandRefresh();
	}

	public void Update(BrowseUpdateFlags flags, bool selectionChanged)
	{
		var scope = selectionChanged ? WindowsShellContextualCommandScope.Selection : WindowsShellContextualCommandScope.None;
		if ((flags & (BrowseUpdateFlags.Location | BrowseUpdateFlags.Items | BrowseUpdateFlags.Loading)) is not 0)
		{
			scope = WindowsShellContextualCommandScope.All;
		}

		if (scope is not WindowsShellContextualCommandScope.None)
		{
			QueueContextualCommandRefresh(scope, coalesceMatchingRequest: scope is WindowsShellContextualCommandScope.Selection);
		}
	}

	public void UpdateSelection(IReadOnlyList<StorableReference> selection)
	{
		QueueContextualCommandRefresh(WindowsShellContextualCommandScope.Selection, selection, coalesceMatchingRequest: true);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) is not 0)
		{
			return;
		}

		_lifetime.Cancel();
		Interlocked.Exchange(ref _contextualCommandRefreshCancellation, null)?.Cancel();
		try
		{
			await _contextualCommandRefreshTask.ConfigureAwait(false);
		}
		finally
		{
			_lifetime.Dispose();
		}
	}

	public bool SupportsShellDragDrop(StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);

		return _windowsSource is not null && reference.SourceId == _windowsSource.SourceId;
	}

	public async Task<WindowsShellDragSource?> PrepareShellDragSourceAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (_windowsSource is null || selection.Count is 0 || selection.Any(reference => reference.SourceId != _windowsSource.SourceId))
		{
			return null;
		}

		return await _windowsSource.DragDrop.PrepareDragSourceAsync(selection, cancellationToken).ConfigureAwait(false);
	}

	public async Task<WindowsShellDropTarget?> PrepareShellDropTargetAsync(StorableReference destination, bool background, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(destination);

		if (_windowsSource is null || destination.SourceId != _windowsSource.SourceId)
		{
			return null;
		}

		return await _windowsSource.DragDrop.PrepareDropTargetAsync(destination, background, cancellationToken).ConfigureAwait(false);
	}

	public Task CopySelectionAsync(bool move, CancellationToken cancellationToken = default)
	{
		return CopyItemsAsync(GetSelectedShellClipboardItems(), move, cancellationToken);
	}

	public async Task CopyItemsAsync(IReadOnlyList<StorableReference> selection, bool move, CancellationToken cancellationToken)
	{
		if (_windowsSource is null || selection.Count is 0)
		{
			throw new NotSupportedException("Only Windows Shell items can be copied to the Windows clipboard.");
		}

		await _windowsSource.Clipboard.SetItemsAsync(selection, move, _ownerWindowHandle, cancellationToken).ConfigureAwait(false);
	}

	public async Task PasteFromClipboardAsync(CancellationToken cancellationToken = default)
	{
		if (_windowsSource is null || !TryGetCurrentShellFolder(out var destinationFolder))
		{
			throw new NotSupportedException("The current location cannot receive Windows Shell clipboard items.");
		}

		if (TryGetCurrentFileSystemFolder(out var fileSystemDestination) && await TryPasteFileSystemClipboardAsync(fileSystemDestination, cancellationToken).ConfigureAwait(false))
		{
			await RefreshAsync(cancellationToken).ConfigureAwait(false);

			return;
		}

		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("{Component}: {Message}", "WindowsBrowseInteraction", "Paste falling back to native Shell paste.");
		}
		if (!await _windowsSource.DragDrop.PasteAsync(destinationFolder, _ownerWindowHandle, cancellationToken).ConfigureAwait(false))
		{
			return;
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<WindowsShellNewItem>> GetNewItemsAsync(CancellationToken cancellationToken = default)
	{
		if (_windowsSource is null || !TryGetCurrentFileSystemFolder(out var folder))
		{
			return [];
		}

		var path = folder.LastKnownAddress!.Value;

		return await GetShellOperations().GetShellNewItemsAsync(path, cancellationToken).ConfigureAwait(false);
	}

	public async Task InvokeNewItemAsync(WindowsShellNewItem item, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (_windowsSource is null || !TryGetCurrentFileSystemFolder(out var folder))
		{
			throw new NotSupportedException("The current location does not expose a Windows Shell New menu.");
		}

		var path = folder.LastKnownAddress!.Value;
		await GetShellOperations().InvokeNewItemAsync(path, item.CommandOffset, item.Name, cancellationToken).ConfigureAwait(false);

		await RefreshAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task<WindowsShellContextMenuSession?> CreateShellContextMenuAsync(IReadOnlyList<StorableReference> selection,
		IReadOnlyList<WindowsShellContextMenuEntry> applicationEntries, WindowsShellContextMenuEntry? expandedMenuEntry, Point clientPoint,
		double rasterizationScale, bool extendedVerbs, CancellationToken cancellationToken)
	{
		if (_windowsSource is null || _ownerWindowHandle is 0)
		{
			return Task.FromResult<WindowsShellContextMenuSession?>(null);
		}

		var point = new System.Drawing.Point(checked((int)Math.Round(clientPoint.X * rasterizationScale)), checked((int)Math.Round(clientPoint.Y * rasterizationScale)));
		if (PInvoke.ClientToScreen(new HWND(_ownerWindowHandle), ref point).Value is 0)
		{
			return Task.FromResult<WindowsShellContextMenuSession?>(null);
		}

		var context = new WindowsShellInvocationContext(_ownerWindowHandle, GetContextualCommandWorkingDirectory(), new(point.X, point.Y));

		return WindowsShellContextMenuSession.CreateAsync(_windowsSource, selection.ToArray(), context, applicationEntries, expandedMenuEntry, extendedVerbs, cancellationToken);
	}

	public CommandState GetContextualCommandState(string commandId, bool hideWhenShellDisabled)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

		if (!_contextualCommands.TryGetValue(commandId, out var command))
		{
			return new(false, false);
		}

		return new(!hideWhenShellDisabled || command.IsEnabled, command.IsEnabled);
	}

	public async Task<bool> InvokeContextualCommandAsync(string commandId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

		if (_shellContextualCommandService is null || _ownerWindowHandle is 0)
		{
			return false;
		}

		if (!await WaitForContextualCommandRefreshAsync(commandId, cancellationToken) || !_contextualCommands.TryGetValue(commandId, out var command) || !command.IsEnabled || IsLoading || IsBusy)
		{
			return false;
		}

		var location = _contextualCommandRefreshLocation;
		var selection = _contextualCommandRefreshSelection.ToArray();
		var context = new WindowsShellInvocationContext(_ownerWindowHandle, GetContextualCommandWorkingDirectory());
		if (!await _shellContextualCommandService.InvokeAsync(location, selection, command, context, cancellationToken).ConfigureAwait(false))
		{
			return false;
		}

		await RefreshAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	public Task<bool> ShowShellPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		return _shellProperties is null ? Task.FromResult(false) : _shellProperties.ShowShellPropertiesAsync(selection.ToArray(), cancellationToken);
	}

	public Task<WindowsShellContextMenuTarget?> GetShellContextMenuTargetAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		return _windowsSource is null
			? Task.FromResult<WindowsShellContextMenuTarget?>(null)
			: WindowsShellContextMenuTarget.CreateAsync(_windowsSource, selection.ToArray(), cancellationToken);
	}

	public bool ShowShellContextMenu(WindowsShellContextMenuTarget target, Point clientPoint, double rasterizationScale)
	{
		ArgumentNullException.ThrowIfNull(target);

		if (_ownerWindowHandle is 0)
		{
			return false;
		}

		var point = new System.Drawing.Point(checked((int)Math.Round(clientPoint.X * rasterizationScale)), checked((int)Math.Round(clientPoint.Y * rasterizationScale)));
		var owner = new HWND(_ownerWindowHandle);
		if (PInvoke.ClientToScreen(owner, ref point).Value is 0)
		{
			return false;
		}

		return new WindowsShellClassicContextMenuSession().Show(owner, target, point);
	}

	private Task RefreshAsync(CancellationToken cancellationToken)
	{
		return _browseAdapter.RefreshAsync(cancellationToken);
	}

	private void QueueContextualCommandRefresh(WindowsShellContextualCommandScope scope = WindowsShellContextualCommandScope.All,
		IReadOnlyList<StorableReference>? selectionOverride = null, bool coalesceMatchingRequest = false)
	{
		if (!_dispatcher.HasThreadAccess)
		{
			if (Volatile.Read(ref _isDisposed) is 0)
			{
				_dispatcher.TryEnqueue(() => QueueContextualCommandRefresh(scope, selectionOverride, coalesceMatchingRequest));
			}

			return;
		}

		var location = (Location as FolderLocation)?.Folder;
		var selection = selectionOverride ?? _pane.BrowseSession.GetSelectedItems().Select(static item => item.Reference).ToArray();
		if (coalesceMatchingRequest && IsSameContextualCommandRequest(location, selection) && !_contextualCommandRefreshTask.IsCompleted)
		{
			return;
		}

		_contextualCommandRefreshLocation = location;
		_contextualCommandRefreshSelection = selection;

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		Interlocked.Exchange(ref _contextualCommandRefreshCancellation, cancellation)?.Cancel();
		MarkContextualCommandsPending(scope);
		if (_shellContextualCommandService is null)
		{
			ApplyContextualCommands([], cancellation);
			_contextualCommandRefreshTask = Task.CompletedTask;
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();

			return;
		}

		if (IsLoading)
		{
			_contextualCommandRefreshTask = Task.CompletedTask;
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();

			return;
		}

		_contextualCommandRefreshTask = LoadContextualCommandsAsync(location, selection, cancellation);
	}

	private bool IsSameContextualCommandRequest(StorableReference? location, IReadOnlyList<StorableReference> selection)
	{
		return Equals(_contextualCommandRefreshLocation, location) && _contextualCommandRefreshSelection.SequenceEqual(selection);
	}

	private async Task<bool> WaitForContextualCommandRefreshAsync(string commandId, CancellationToken cancellationToken)
	{
		while (_pendingContextualCommandIds.Contains(commandId))
		{
			var refreshTask = _contextualCommandRefreshTask;
			if (refreshTask.IsCompleted)
			{
				return false;
			}

			await refreshTask.WaitAsync(cancellationToken);
		}

		return true;
	}

	private async Task LoadContextualCommandsAsync(StorableReference? location, IReadOnlyList<StorableReference> selection, CancellationTokenSource cancellation)
	{
		try
		{
			var commands = await _shellContextualCommandService!.GetCommandsAsync(location, selection, _ownerWindowHandle, cancellation.Token).ConfigureAwait(false);
			if (cancellation.IsCancellationRequested || Volatile.Read(ref _isDisposed) is not 0)
			{
				return;
			}

			await ApplyContextualCommandsAsync(commands, cancellation).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug("{Component}: {Message}", "WindowsBrowseInteraction", $"Contextual commands ERROR type={exception.GetType().Name}");
			}
			await CompleteContextualCommandRefreshAsync(cancellation).ConfigureAwait(false);
		}
		finally
		{
			Interlocked.CompareExchange(ref _contextualCommandRefreshCancellation, null, cancellation);
			cancellation.Dispose();
		}
	}

	private Task ApplyContextualCommandsAsync(IReadOnlyList<WindowsShellContextualCommand> commands, CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			ApplyContextualCommands(commands, cancellation);

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			ApplyContextualCommands(commands, cancellation);
			completion.SetResult();
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected contextual command updates."));
		}

		return completion.Task;
	}

	private void ApplyContextualCommands(IReadOnlyList<WindowsShellContextualCommand> commands, CancellationTokenSource cancellation)
	{
		if (cancellation.IsCancellationRequested || !ReferenceEquals(_contextualCommandRefreshCancellation, cancellation) || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		var nextCommands = commands.ToDictionary(static command => command.Id, StringComparer.OrdinalIgnoreCase);
		var changedCommandIds = GetChangedContextualCommandIds(_contextualCommands, nextCommands, _pendingContextualCommandIds);
		_contextualCommands = nextCommands;
		_pendingContextualCommandIds.Clear();
		CommandManager.RefreshContextualStates(changedCommandIds);
	}

	private Task CompleteContextualCommandRefreshAsync(CancellationTokenSource cancellation)
	{
		if (_dispatcher.HasThreadAccess)
		{
			CompleteContextualCommandRefresh(cancellation);

			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(() =>
		{
			CompleteContextualCommandRefresh(cancellation);
			completion.SetResult();
		}))
		{
			completion.SetException(new InvalidOperationException("The Files UI dispatcher rejected contextual command completion."));
		}

		return completion.Task;
	}

	private void CompleteContextualCommandRefresh(CancellationTokenSource cancellation)
	{
		if (cancellation.IsCancellationRequested || !ReferenceEquals(_contextualCommandRefreshCancellation, cancellation) || Volatile.Read(ref _isDisposed) is not 0)
		{
			return;
		}

		ApplyContextualCommands([], cancellation);
	}

	private void MarkContextualCommandsPending(WindowsShellContextualCommandScope scope)
	{
		foreach (var command in _contextualCommands.Values)
		{
			if ((command.Scope & scope) is not WindowsShellContextualCommandScope.None)
			{
				_pendingContextualCommandIds.Add(command.Id);
			}
		}
	}

	private static IReadOnlySet<string> GetChangedContextualCommandIds(IReadOnlyDictionary<string, WindowsShellContextualCommand> currentCommands,
		IReadOnlyDictionary<string, WindowsShellContextualCommand> nextCommands, IEnumerable<string> pendingCommandIds)
	{
		var changedCommandIds = pendingCommandIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in currentCommands)
		{
			if (!nextCommands.TryGetValue(pair.Key, out var nextCommand) || pair.Value.IsEnabled != nextCommand.IsEnabled || pair.Value.Scope != nextCommand.Scope)
			{
				changedCommandIds.Add(pair.Key);
			}
		}

		foreach (var commandId in nextCommands.Keys)
		{
			if (!currentCommands.ContainsKey(commandId))
			{
				changedCommandIds.Add(commandId);
			}
		}

		return changedCommandIds;
	}

	private string? GetContextualCommandWorkingDirectory()
	{
		return Location is FolderLocation { Folder.LastKnownAddress: { } address } && address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			? address.Value
			: null;
	}

	private IReadOnlyList<StorableReference> GetSelectedShellClipboardItems()
	{
		var selectedItems = _pane.BrowseSession.GetSelectedItems();
		var selection = new List<StorableReference>(selectedItems.Count);
		foreach (var item in selectedItems)
		{
			if (_windowsSource is null || item.Reference.SourceId != _windowsSource.SourceId)
			{
				return [];
			}

			selection.Add(item.Reference);
		}

		return selection;
	}

	private bool TryGetCurrentShellFolder(out StorableReference folder)
	{
		if (_windowsSource is not null && Location is FolderLocation { Folder: var candidate } && candidate.SourceId == _windowsSource.SourceId)
		{
			folder = candidate;

			return true;
		}

		folder = null!;

		return false;
	}

	private static bool HasClipboardStorageItems()
	{
		try
		{
			if (Clipboard.GetContent().Contains(StandardDataFormats.StorageItems))
			{
				return true;
			}
		}
		catch
		{
		}

		try
		{
			return TryGetNativeClipboardFilePaths(out _);
		}
		catch
		{
			return false;
		}
	}

#pragma warning disable RS0030 // Windows Storage Items are required for clipboard interoperability.
	private async Task<bool> TryPasteFileSystemClipboardAsync(StorableReference destinationFolder, CancellationToken cancellationToken)
	{
		DataPackageView? data = null;
		var paths = new List<string>();
		try
		{
			data = Clipboard.GetContent();
			if (data.Contains(StandardDataFormats.StorageItems))
			{
				IReadOnlyList<Windows.Storage.IStorageItem> storageItems;
				try
				{
					storageItems = await data.GetStorageItemsAsync();
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch
				{
					storageItems = [];
				}

				foreach (var storageItem in storageItems)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (!string.IsNullOrWhiteSpace(storageItem.Path))
						{
							paths.Add(storageItem.Path);
						}
					}
					catch
					{
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			data = null;
		}

		IReadOnlyList<string> nativePaths = [];
		var hasNativePaths = paths.Count is not 0 || TryGetNativeClipboardFilePaths(out nativePaths);
		if (!hasNativePaths)
		{
			return false;
		}

		if (paths.Count is 0)
		{
			paths.AddRange(nativePaths);
		}

		if (paths.Count is 0)
		{
			return false;
		}

		var move = data?.RequestedOperation.HasFlag(DataPackageOperation.Move) is true;
		if (_logger.IsEnabled(LogLevel.Debug))
		{
			_logger.LogDebug("{Component}: {Message}", "WindowsBrowseInteraction", $"Paste server kind={(move ? "Move" : "Copy")} items={paths.Count}");
		}
		var addresses = paths.Select(static path => new StorageAddress(WindowsStorageSource.FileAddressScheme, path)).ToArray();
		var result = await _pane.TransferAddressesAsync(addresses, destinationFolder, move, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw result.Error!;
		}

		return true;
	}
#pragma warning restore RS0030

	private static unsafe bool TryGetNativeClipboardFilePaths(out IReadOnlyList<string> paths)
	{
		paths = [];
		if (PInvoke.OleGetClipboard(out var dataObject).Failed || dataObject is null)
		{
			return false;
		}

		var format = default(FORMATETC);
		format.cfFormat = 15;
		format.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
		format.lindex = -1;
		format.tymed = (uint)TYMED.TYMED_HGLOBAL;
		if (dataObject.GetData(in format, out var medium).Failed)
		{
			return false;
		}

		try
		{
			if (medium.tymed is not TYMED.TYMED_HGLOBAL || medium.u.hGlobal.IsNull)
			{
				return false;
			}

			var size = PInvoke.GlobalSize(medium.u.hGlobal);
			if (size < 20)
			{
				return false;
			}

			var buffer = PInvoke.GlobalLock(medium.u.hGlobal);
			if (buffer is null)
			{
				return false;
			}

			try
			{
				var offset = Marshal.ReadInt32((nint)buffer);
				var isWide = Marshal.ReadInt32((nint)buffer, 16) is not 0;
				if (offset < 20 || (nuint)offset >= size || !isWide)
				{
					return false;
				}

				var remaining = checked((int)size - offset);
				var current = (nint)buffer + offset;
				var result = new List<string>();
				while (remaining >= sizeof(char))
				{
					var length = 0;
					while ((length + 1) * sizeof(char) <= remaining && Marshal.ReadInt16(current, length * sizeof(char)) is not 0)
					{
						length++;
					}

					if (length is 0)
					{
						break;
					}

					result.Add(Marshal.PtrToStringUni(current, length));
					var advance = checked((length + 1) * sizeof(char));
					current += advance;
					remaining -= advance;
				}

				paths = result;

				return result.Count is not 0;
			}
			finally
			{
				_ = PInvoke.GlobalUnlock(medium.u.hGlobal);
			}
		}
		finally
		{
			PInvoke.ReleaseStgMedium(ref medium);
		}
	}

	private bool TryGetCurrentFileSystemFolder(out StorableReference folder)
	{
		if (Location is FolderLocation { Folder: { LastKnownAddress: { } address } }
			&& address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase)
			&& Path.IsPathRooted(address.Value))
		{
			folder = ((FolderLocation)Location).Folder;

			return true;
		}

		folder = null!;

		return false;
	}

	private IWindowsShellOperationService GetShellOperations()
	{
		return _shellOperations ?? throw new NotSupportedException("Windows Shell operations are not available.");
	}


}

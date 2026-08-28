// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Resolves and invokes the fixed contextual commands exposed by the Windows Shell.
/// </summary>
public sealed class WindowsShellContextualCommandService
{
	private const uint ContextMenuQueryFlags = 0x00000100 | 0x00000800;
	private const string CommandStoreRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";
	private const string RestoreAllRecycleBinBackendId = "Windows.RecycleBin.RestoreAll";
	private static readonly string[] _selectionExplorerCommandIds = ["Windows.Zip.Action", "Windows.PinToHome", "Windows.PinToHomeFile"];
	private static readonly string[] _locationExplorerCommandIds = ["Windows.PinToHome"];
	private static readonly IReadOnlyDictionary<string, string> _commandIdsByBackendId = CreateCommandIdMap();
	private readonly WindowsStorageSource _source;
	private readonly WindowsShellAppExtensionService _appExtensions;

	/// <summary>
	/// Initializes a contextual Windows Shell command service.
	/// </summary>
	/// <param name="source">The Windows storage source used to resolve command targets.</param>
	public WindowsShellContextualCommandService(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
		_appExtensions = new(source);
	}

	/// <summary>
	/// Gets the contextual Shell commands applicable to a location and selection.
	/// </summary>
	/// <param name="location">The current folder reference, when the view has one.</param>
	/// <param name="selection">The selected item references.</param>
	/// <param name="ownerWindowHandle">The owner window used when constructing folder background commands.</param>
	/// <param name="cancellationToken">The token used to cancel command discovery.</param>
	/// <returns>Apartment-neutral command descriptions keyed by stable Shell command identifiers.</returns>
	public async Task<IReadOnlyList<WindowsShellContextualCommand>> GetCommandsAsync(StorableReference? location, IReadOnlyList<StorableReference> selection,
		nint ownerWindowHandle, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		var commands = new Dictionary<string, WindowsShellContextualCommand>(StringComparer.OrdinalIgnoreCase);
		var isRecycleBin = false;
		if (selection.Count is not 0)
		{
			var appExtensionCommands = await _appExtensions.GetCommandsAsync(selection, cancellationToken).ConfigureAwait(false);
			AppendAppExtensionCommands(appExtensionCommands, commands);
			var registeredCommands = await GetRegisteredCommandsAsync(selection, _selectionExplorerCommandIds, cancellationToken).ConfigureAwait(false);
			AppendAppExtensionCommands(registeredCommands, commands);

			if (await ResolveLocatorsAsync(selection, cancellationToken).ConfigureAwait(false) is { } selectionLocators)
			{
				var contextMenuCommands = await _source.Scheduler.InvokeOperationAsync(
					() => GetContextMenuCommands(WindowsShellItemArrayFactory.Create(selectionLocators), WindowsShellContextMenuTargetKind.Selection), cancellationToken).ConfigureAwait(false);
				AppendCommands(contextMenuCommands, commands);
			}
		}

		if (location is not null && await ResolveLocatorAsync(location, cancellationToken).ConfigureAwait(false) is { } locationLocator)
		{
			isRecycleBin = await _source.ShellItemResolver.InvokeOperationAsync(locationLocator, IsRecycleBin, cancellationToken).ConfigureAwait(false);
			if (selection.Count is 0)
			{
				var registeredCommands = await GetRegisteredCommandsAsync([location], _locationExplorerCommandIds, cancellationToken).ConfigureAwait(false);
				AppendAppExtensionCommands(registeredCommands, commands);
				var locationCommands = await _source.Scheduler.InvokeOperationAsync(
					() => GetContextMenuCommands(WindowsShellItemArrayFactory.Create([locationLocator]), WindowsShellContextMenuTargetKind.LocationItem), cancellationToken).ConfigureAwait(false);
				AppendCommands(locationCommands, commands);
			}

			if (isRecycleBin)
			{
				var hasItems = await HasRecycleBinItemsAsync(cancellationToken).ConfigureAwait(false);
				commands[WindowsShellContextualCommandIds.EmptyRecycleBin] = new(WindowsShellContextualCommandIds.EmptyRecycleBin, hasItems, new WindowsShellEmptyRecycleBinContextualCommandToken());
				commands[WindowsShellContextualCommandIds.RestoreAllRecycleBinItems] = new(
					WindowsShellContextualCommandIds.RestoreAllRecycleBinItems, hasItems, new WindowsShellCommandStoreContextualCommandToken(RestoreAllRecycleBinBackendId));
				var backgroundCommands = await _source.ShellItemResolver.InvokeOperationAsync(
					locationLocator, shellItem => GetFolderBackgroundCommands(shellItem, ownerWindowHandle), cancellationToken).ConfigureAwait(false);
				AppendCommands(backgroundCommands, commands);
			}
		}

		return isRecycleBin ? commands.Values.Where(static command => IsRecycleBinCommand(command.Id)).ToArray() : commands.Values.ToArray();
	}

	/// <summary>
	/// Invokes a contextual Shell command against the current location and selection.
	/// </summary>
	/// <param name="location">The current folder reference, when the view has one.</param>
	/// <param name="selection">The selected item references.</param>
	/// <param name="command">The previously resolved command description.</param>
	/// <param name="context">The owner window and working-directory context.</param>
	/// <param name="cancellationToken">The token used to cancel command preparation.</param>
	/// <returns><see langword="true"/> when the Shell command was invoked.</returns>
	public async Task<bool> InvokeAsync(
		StorableReference? location, IReadOnlyList<StorableReference> selection, WindowsShellContextualCommand command, WindowsShellInvocationContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(command);
		ArgumentNullException.ThrowIfNull(context);

		return command.Token switch
		{
			WindowsShellAppExtensionContextualCommandToken appExtension => await _appExtensions.InvokeAsync(selection, appExtension.Command, cancellationToken).ConfigureAwait(false),
			WindowsShellContextMenuContextualCommandToken contextMenu => await InvokeContextMenuCommandAsync(
				location, selection, command.Id, contextMenu.TargetKind, context, cancellationToken).ConfigureAwait(false),
			WindowsShellCommandStoreContextualCommandToken commandStore => await InvokeCommandStoreCommandAsync(location, commandStore.BackendId, context, cancellationToken).ConfigureAwait(false),
			WindowsShellEmptyRecycleBinContextualCommandToken => await EmptyRecycleBinAsync(context, cancellationToken).ConfigureAwait(false),
			_ => false,
		};
	}

	private static IReadOnlyDictionary<string, string> CreateCommandIdMap()
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		AddBackendIds(map, WindowsShellContextualCommandIds.Mount, "mount", "Windows.mount", "{2D233648-EEAF-450A-A306-4B1239AD6BBF}");
		AddBackendIds(map, WindowsShellContextualCommandIds.BurnDiscImage, "burn", "Windows.DiscImage.burn", "{5A6D5871-AD2A-4E06-8637-065A8062CF01}");
		AddBackendIds(map, WindowsShellContextualCommandIds.EmptyRecycleBin, "empty", "emptyrecyclebin", "Windows.RecycleBin.Empty");
		AddBackendIds(map, WindowsShellContextualCommandIds.RestoreAllRecycleBinItems, "restoreall", "Windows.RecycleBin.RestoreAll", "{F123C134-68E1-427B-B1BE-87CD57C73E7C}");
		AddBackendIds(map, WindowsShellContextualCommandIds.RestoreRecycleBinItems, "restore", "restoreitems", "undelete", "Windows.RecycleBin.RestoreItems", "{C565921A-1E6E-11E0-BA70-462ADFD72085}");
		AddBackendIds(map, WindowsShellContextualCommandIds.CompressToZip, "Windows.Zip.Action", "{9A25CA3B-A076-491C-B953-90A8048E6EE7}");
		AddBackendIds(map, WindowsShellContextualCommandIds.PinToQuickAccess, "pintohome", "Windows.PinToHome", "{19CF0569-6C80-4774-911C-CB6463844355}");
		AddBackendIds(map, WindowsShellContextualCommandIds.AddToFavorites, "pintohomefile", "Windows.PinToHomeFile", "{15ABAD0C-89F8-4377-AA54-2B4E515E2A55}");
		AddBackendIds(map, WindowsShellContextualCommandIds.CopyAsPath, "copyaspath", "Windows.CopyAsPath", "{707C7BC6-685A-4A4D-A275-3966A5A3EFAA}");

		return map;
	}

	private static void AddBackendIds(IDictionary<string, string> map, string commandId, params string[] backendIds)
	{
		foreach (var backendId in backendIds)
		{
			map[backendId] = commandId;
		}
	}

	private static void AppendAppExtensionCommands(IEnumerable<WindowsShellAppExtensionCommand> source, IDictionary<string, WindowsShellContextualCommand> destination)
	{
		foreach (var command in source)
		{
			if (TryMapCommandId(command.Id, out var commandId))
			{
				destination.TryAdd(commandId, new(commandId, command.IsEnabled, new WindowsShellAppExtensionContextualCommandToken(command)));
			}

			AppendAppExtensionCommands(command.Children, destination);
		}
	}

	private static void AppendCommands(IEnumerable<WindowsShellContextualCommand> source, IDictionary<string, WindowsShellContextualCommand> destination)
	{
		foreach (var command in source)
		{
			destination.TryAdd(command.Id, command);
		}
	}

	private static bool TryMapCommandId(string backendId, out string commandId)
	{
		return _commandIdsByBackendId.TryGetValue(backendId, out commandId!);
	}

	private static bool IsRecycleBinCommand(string commandId)
	{
		return commandId is WindowsShellContextualCommandIds.EmptyRecycleBin or WindowsShellContextualCommandIds.RestoreAllRecycleBinItems or WindowsShellContextualCommandIds.RestoreRecycleBinItems;
	}

	private async Task<IReadOnlyList<WindowsShellAppExtensionCommand>> GetRegisteredCommandsAsync(IReadOnlyList<StorableReference> selection,
		IReadOnlyList<string> backendIds, CancellationToken cancellationToken)
	{
		var registrations = backendIds.Select(TryGetCommandStoreRegistration).OfType<WindowsFileExplorerAppExtensionRegistration>().ToArray();

		return await _appExtensions.GetRegisteredCommandsAsync(selection, registrations, cancellationToken).ConfigureAwait(false);
	}

	private static WindowsFileExplorerAppExtensionRegistration? TryGetCommandStoreRegistration(string backendId)
	{
		try
		{
			using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using var commandKey = baseKey.OpenSubKey($@"{CommandStoreRegistryPath}\{backendId}");
			if (commandKey?.GetValue("ExplorerCommandHandler") is not string handlerId || !Guid.TryParse(handlerId, out var classId))
			{
				return null;
			}

			return new(classId, backendId, commandKey.GetValue(null) as string ?? backendId, string.Empty);
		}
		catch (Exception exception) when (exception is IOException or System.Security.SecurityException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static IReadOnlyList<WindowsShellContextualCommand> GetContextMenuCommands(IShellItemArray selection, WindowsShellContextMenuTargetKind targetKind)
	{
		try
		{
			var contextMenu = WindowsShellContextMenuCommandHelper.Create(selection);

			return GetContextMenuCommands(contextMenu, targetKind);
		}
		catch (Exception exception) when (exception is COMException or Win32Exception)
		{
			return [];
		}
	}

	private static IReadOnlyList<WindowsShellContextualCommand> GetFolderBackgroundCommands(IShellItem shellItem, nint ownerWindowHandle)
	{
		try
		{
			shellItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var folder).ThrowOnFailure();
			folder.CreateViewObject<IContextMenu>((HWND)ownerWindowHandle, out var contextMenu).ThrowOnFailure();

			return GetContextMenuCommands(contextMenu, WindowsShellContextMenuTargetKind.LocationBackground);
		}
		catch (Exception exception) when (exception is COMException or Win32Exception)
		{
			return [];
		}
	}

	private static IReadOnlyList<WindowsShellContextualCommand> GetContextMenuCommands(IContextMenu contextMenu, WindowsShellContextMenuTargetKind targetKind)
	{
		using var menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		if (contextMenu.QueryContextMenu(menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId, WindowsShellContextMenuCommandHelper.LastCommandId, ContextMenuQueryFlags).Failed)
		{
			return [];
		}

		var commands = new Dictionary<string, WindowsShellContextualCommand>(StringComparer.OrdinalIgnoreCase);
		AppendMenuCommands(menu, contextMenu, targetKind, commands);

		return commands.Values.ToArray();
	}

	private static unsafe void AppendMenuCommands(DestroyMenuSafeHandle menu, IContextMenu contextMenu, WindowsShellContextMenuTargetKind targetKind,
		IDictionary<string, WindowsShellContextualCommand> commands)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = new MENUITEMINFOW { cbSize = (uint)sizeof(MENUITEMINFOW), fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_SUBMENU };
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, ref item))
			{
				continue;
			}

			AppendMenuCommand(item, contextMenu, targetKind, commands);
		}
	}

	private static unsafe void AppendSubMenuCommands(HMENU menu, IContextMenu contextMenu, WindowsShellContextMenuTargetKind targetKind,
		IDictionary<string, WindowsShellContextualCommand> commands)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = new MENUITEMINFOW { cbSize = (uint)sizeof(MENUITEMINFOW), fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_SUBMENU };
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, &item))
			{
				continue;
			}

			AppendMenuCommand(item, contextMenu, targetKind, commands);
		}
	}

	private static void AppendMenuCommand(MENUITEMINFOW item, IContextMenu contextMenu, WindowsShellContextMenuTargetKind targetKind,
		IDictionary<string, WindowsShellContextualCommand> commands)
	{
		if (!item.hSubMenu.IsNull)
		{
			AppendSubMenuCommands(item.hSubMenu, contextMenu, targetKind, commands);
		}

		if (item.wID < WindowsShellContextMenuCommandHelper.FirstCommandId || item.wID > WindowsShellContextMenuCommandHelper.LastCommandId)
		{
			return;
		}

		var ordinal = item.wID - WindowsShellContextMenuCommandHelper.FirstCommandId;
		var backendId = WindowsShellContextMenuCommandHelper.GetCanonicalVerb(contextMenu, ordinal);
		if (backendId is null || !TryMapCommandId(backendId, out var commandId))
		{
			return;
		}

		var isEnabled = !item.fState.HasFlag(MENU_ITEM_STATE.MFS_DISABLED);
		commands.TryAdd(commandId, new(commandId, isEnabled, new WindowsShellContextMenuContextualCommandToken(targetKind)));
	}

	private async Task<bool> HasRecycleBinItemsAsync(CancellationToken cancellationToken)
	{
		return await _source.Scheduler.InvokeConcurrentAsync(
			() =>
			{
				var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };

				return PInvoke.SHQueryRecycleBin(null, ref info).Succeeded && info.i64NumItems > 0;
			}, cancellationToken).ConfigureAwait(false);
	}

	private async Task<bool> InvokeContextMenuCommandAsync(StorableReference? location, IReadOnlyList<StorableReference> selection, string commandId,
		WindowsShellContextMenuTargetKind targetKind, WindowsShellInvocationContext context, CancellationToken cancellationToken)
	{
		if (targetKind is WindowsShellContextMenuTargetKind.Selection)
		{
			if (await ResolveLocatorsAsync(selection, cancellationToken).ConfigureAwait(false) is not { } selectionLocators)
			{
				return false;
			}

			return await _source.Scheduler.InvokeOperationAsync(
				() => InvokeContextMenuCommand(WindowsShellContextMenuCommandHelper.Create(WindowsShellItemArrayFactory.Create(selectionLocators)), commandId, context), cancellationToken).ConfigureAwait(false);
		}

		if (location is null || await ResolveLocatorAsync(location, cancellationToken).ConfigureAwait(false) is not { } locationLocator)
		{
			return false;
		}

		return await _source.ShellItemResolver.InvokeOperationAsync(
			locationLocator,
			shellItem => targetKind is WindowsShellContextMenuTargetKind.LocationBackground
				? InvokeFolderBackgroundCommand(shellItem, commandId, context)
				: InvokeContextMenuCommand(WindowsShellContextMenuCommandHelper.Create(shellItem), commandId, context), cancellationToken).ConfigureAwait(false);
	}

	private async Task<bool> InvokeCommandStoreCommandAsync(StorableReference? location, string backendId, WindowsShellInvocationContext context, CancellationToken cancellationToken)
	{
		if (location is null || await ResolveLocatorAsync(location, cancellationToken).ConfigureAwait(false) is not { } locationLocator)
		{
			return false;
		}

		return await _source.Scheduler.InvokeOperationAsync(() => InvokeCommandStoreCommand(locationLocator, backendId, context), cancellationToken).ConfigureAwait(false);
	}

	private static unsafe bool InvokeCommandStoreCommand(WindowsItemLocator location, string backendId, WindowsShellInvocationContext context)
	{
		if (TryGetCommandStoreDelegateExecuteClassId(backendId) is not { } classId || WindowsShellCommandStorePropertyBag.TryCreate(backendId) is not { } propertyBag)
		{
			return false;
		}

		var createResult = PInvoke.CoCreateInstance(classId, null, CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_LOCAL_SERVER, out IExecuteCommand? executeCommand);
		if (createResult.Failed || executeCommand is not IInitializeCommand initializeCommand || executeCommand is not IObjectWithSelection objectWithSelection)
		{
			return false;
		}

		fixed (char* commandName = backendId)
		{
			if (initializeCommand.Initialize(new PCWSTR(commandName), propertyBag).Failed)
			{
				return false;
			}
		}

		var shellItems = WindowsShellItemArrayFactory.Create([location]);
		if (objectWithSelection.SetSelection(shellItems).Failed)
		{
			return false;
		}

		if (context.WorkingDirectory is { } workingDirectory)
		{
			_ = executeCommand.SetDirectory(workingDirectory);
		}

		if (context.InvocationPoint is { } invocationPoint)
		{
			_ = executeCommand.SetPosition(new System.Drawing.Point(invocationPoint.X, invocationPoint.Y));
		}

		_ = executeCommand.SetShowWindow((int)SHOW_WINDOW_CMD.SW_SHOWNORMAL);

		return executeCommand.Execute().Succeeded;
	}

	private static Guid? TryGetCommandStoreDelegateExecuteClassId(string backendId)
	{
		try
		{
			using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using var commandKey = baseKey.OpenSubKey($@"{CommandStoreRegistryPath}\{backendId}\command");

			return commandKey?.GetValue("DelegateExecute") is string classId && Guid.TryParse(classId, out var parsedClassId) ? parsedClassId : null;
		}
		catch (Exception exception) when (exception is IOException or System.Security.SecurityException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool InvokeFolderBackgroundCommand(IShellItem shellItem, string commandId, WindowsShellInvocationContext context)
	{
		shellItem.BindToHandler<IShellFolder>(null, PInvoke.BHID_SFObject, out var folder).ThrowOnFailure();
		folder.CreateViewObject<IContextMenu>((HWND)context.OwnerWindowHandle, out var contextMenu).ThrowOnFailure();

		return InvokeContextMenuCommand(contextMenu, commandId, context);
	}

	private static bool InvokeContextMenuCommand(IContextMenu contextMenu, string commandId, WindowsShellInvocationContext context)
	{
		using var menu = WindowsShellContextMenuCommandHelper.CreateMenu();
		contextMenu.QueryContextMenu(menu, 0, WindowsShellContextMenuCommandHelper.FirstCommandId, WindowsShellContextMenuCommandHelper.LastCommandId, ContextMenuQueryFlags).ThrowOnFailure();
		if (!TryFindCommandOrdinal(menu, contextMenu, commandId, out var ordinal))
		{
			return false;
		}

		WindowsShellContextMenuCommandHelper.Invoke(contextMenu, ordinal, context);

		return true;
	}

	private static unsafe bool TryFindCommandOrdinal(DestroyMenuSafeHandle menu, IContextMenu contextMenu, string commandId, out uint ordinal)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = new MENUITEMINFOW { cbSize = (uint)sizeof(MENUITEMINFOW), fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_SUBMENU };
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, ref item))
			{
				continue;
			}

			if (TryMatchCommand(item, contextMenu, commandId, out ordinal))
			{
				return true;
			}
		}

		ordinal = 0;

		return false;
	}

	private static unsafe bool TryFindCommandOrdinal(HMENU menu, IContextMenu contextMenu, string commandId, out uint ordinal)
	{
		var itemCount = PInvoke.GetMenuItemCount(menu);
		for (var index = 0; index < itemCount; index++)
		{
			var item = new MENUITEMINFOW { cbSize = (uint)sizeof(MENUITEMINFOW), fMask = MENU_ITEM_MASK.MIIM_ID | MENU_ITEM_MASK.MIIM_SUBMENU };
			if (!PInvoke.GetMenuItemInfo(menu, checked((uint)index), true, &item))
			{
				continue;
			}

			if (TryMatchCommand(item, contextMenu, commandId, out ordinal))
			{
				return true;
			}
		}

		ordinal = 0;

		return false;
	}

	private static bool TryMatchCommand(MENUITEMINFOW item, IContextMenu contextMenu, string commandId, out uint ordinal)
	{
		if (!item.hSubMenu.IsNull && TryFindCommandOrdinal(item.hSubMenu, contextMenu, commandId, out ordinal))
		{
			return true;
		}

		if (item.wID >= WindowsShellContextMenuCommandHelper.FirstCommandId && item.wID <= WindowsShellContextMenuCommandHelper.LastCommandId)
		{
			var candidateOrdinal = item.wID - WindowsShellContextMenuCommandHelper.FirstCommandId;
			var backendId = WindowsShellContextMenuCommandHelper.GetCanonicalVerb(contextMenu, candidateOrdinal);
			if (backendId is not null && TryMapCommandId(backendId, out var candidateId) && candidateId.Equals(commandId, StringComparison.OrdinalIgnoreCase))
			{
				ordinal = candidateOrdinal;

				return true;
			}
		}

		ordinal = 0;

		return false;
	}

	private static bool IsRecycleBin(IShellItem shellItem)
	{
		if (PInvoke.SHGetKnownFolderItem(PInvoke.FOLDERID_RecycleBinFolder, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out IShellItem recycleBin).Failed)
		{
			return false;
		}

		return shellItem.Compare(recycleBin, unchecked((uint)_SICHINTF.SICHINT_ALLFIELDS), out var order).Succeeded && order is 0;
	}

	private async Task<IReadOnlyList<WindowsItemLocator>?> ResolveLocatorsAsync(IReadOnlyList<StorableReference> references, CancellationToken cancellationToken)
	{
		var locators = new List<WindowsItemLocator>(references.Count);
		foreach (var reference in references)
		{
			if (await ResolveLocatorAsync(reference, cancellationToken).ConfigureAwait(false) is not { } locator)
			{
				return null;
			}

			locators.Add(locator);
		}

		return locators;
	}

	private async Task<WindowsItemLocator?> ResolveLocatorAsync(StorableReference reference, CancellationToken cancellationToken)
	{
		if (reference.SourceId != _source.SourceId)
		{
			return null;
		}

		return await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false) is WindowsStorable item ? item.Locator : null;
	}

	private async Task<bool> EmptyRecycleBinAsync(WindowsShellInvocationContext context, CancellationToken cancellationToken)
	{
		return await _source.Scheduler.InvokeOperationAsync(
			() => PInvoke.SHEmptyRecycleBin((HWND)context.OwnerWindowHandle, null, 0).Succeeded, cancellationToken).ConfigureAwait(false);
	}
}

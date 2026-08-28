// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Capabilities.Thumbnails;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Provides Windows Shell app-extension commands and property pages without constructing an <c>IContextMenu</c>.
/// </summary>
public sealed class WindowsShellAppExtensionService
{
	private const int MaximumCommandIconPixelSize = 256;
	private const int MaximumSubCommandDepth = 2;
	private const int PendingResult = unchecked((int)0x8000000A);
	private readonly WindowsStorageSource _source;
	private readonly WindowsShellThumbnailBackend _thumbnailBackend = new();

	/// <summary>Initializes a File Explorer app-extension service.</summary>
	/// <param name="source">The Windows Shell storage source used to resolve selections.</param>
	public WindowsShellAppExtensionService(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>Gets the packaged File Explorer commands applicable to a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel enumeration.</param>
	/// <returns>Apartment-neutral command descriptions.</returns>
	public async Task<IReadOnlyList<WindowsShellAppExtensionCommand>> GetCommandsAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return [];
		}

		return await _source.Scheduler.InvokeOperationAsync(() => GetCommandsOnCurrentSta(resolvedSelection, cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Gets a command icon at the requested physical pixel size.</summary>
	/// <param name="command">The command that supplies the Shell icon resource.</param>
	/// <param name="pixelSize">The square icon size in physical pixels.</param>
	/// <param name="cancellationToken">The token used to cancel icon extraction.</param>
	/// <returns>The encoded PNG, or an empty value when the command has no icon.</returns>
	public Task<ReadOnlyMemory<byte>> GetCommandIconAsync(WindowsShellAppExtensionCommand command, int pixelSize, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, MaximumCommandIconPixelSize);

		return command.IconPath is not { Length: > 0 } iconPath
			? Task.FromResult(ReadOnlyMemory<byte>.Empty)
			: _source.Scheduler.InvokeConcurrentAsync(() => WindowsShellIconProvider.GetResourceIcon(iconPath, command.IconIndex, pixelSize, cancellationToken), cancellationToken);
	}

	/// <summary>Gets the Windows Shell property pages applicable to a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution.</param>
	/// <returns>Apartment-neutral descriptions of the ReFiles pages that apply to the selection.</returns>
	public async Task<IReadOnlyList<WindowsShellPropertyPage>> GetPropertyPagesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return [];
		}

		return WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection);
	}

	/// <summary>Gets a portable target that can create the selection's classic Shell context menu on the UI STA.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution.</param>
	/// <returns>The copied item ID lists, or <see langword="null"/> when the selection cannot expose one native menu.</returns>
	public async Task<WindowsShellContextMenuTarget?> GetContextMenuTargetAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection
			|| !HasCommonParent(resolvedSelection.Locators))
		{
			return null;
		}

		var absolutePidls = resolvedSelection.Locators.Select(static locator => (ReadOnlyMemory<byte>)locator.AbsolutePidl.ToArray()).ToArray();

		return new WindowsShellContextMenuTarget(absolutePidls);
	}

	/// <summary>Gets the native data used to render the Windows property pages for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral page data, or <see langword="null"/> when the selection cannot be resolved.</returns>
	public async Task<WindowsShellPropertySheetData?> GetPropertySheetDataAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return null;
		}

		return await _source.Scheduler.InvokeConcurrentAsync(
			() => ReadPropertySheetDataOnCurrentSta(resolvedSelection, WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection), cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Gets the native data used to render one Windows property page for a selection.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="kind">The property page to read.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and property retrieval.</param>
	/// <returns>Apartment-neutral data for the requested page, or <see langword="null"/> when the page does not apply or the selection cannot be resolved.</returns>
	public async Task<WindowsShellPropertySheetData?> GetPropertyPageDataAsync(
		IReadOnlyList<StorableReference> selection, WindowsShellPropertyPageKind kind, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return null;
		}

		var page = WindowsShellPropertyPageEnumerator.GetPages(resolvedSelection).FirstOrDefault(page => page.Kind == kind);
		if (page is null)
		{
			return null;
		}

		return await _source.Scheduler.InvokeConcurrentAsync(() => ReadPropertySheetDataOnCurrentSta(resolvedSelection, [page], cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Gets the Shell description and icon shown on the General property page for a single item.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution and icon extraction.</param>
	/// <returns>The description and icon, or empty values when the selection cannot be resolved to one item.</returns>
	public async Task<(string? Description, ThumbnailResult? Icon)> GetGeneralPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is not 1 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return (null, null);
		}

		var locator = resolvedSelection.Locators[0];

		return await _source.ShellItemResolver.InvokeConcurrentAsync(locator, shellItem => ReadGeneralProperties(shellItem, locator, cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Invokes a previously described packaged File Explorer command.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="command">The command to invoke.</param>
	/// <param name="cancellationToken">The token used to cancel activation.</param>
	/// <returns><see langword="true"/> when the command was invoked.</returns>
	public async Task<bool> InvokeAsync(IReadOnlyList<StorableReference> selection, WindowsShellAppExtensionCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(command);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return false;
		}

		return await _source.Scheduler.InvokeOperationAsync(() => InvokeOnCurrentSta(resolvedSelection, command.Token), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Opens the Shell property sheet for a selection without constructing an <c>IContextMenu</c>.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="cancellationToken">The token used to cancel preparation.</param>
	/// <returns><see langword="true"/> when the property sheet request was accepted.</returns>
	public async Task<bool> ShowShellPropertiesAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return false;
		}

		return await _source.Scheduler.InvokeOperationAsync(() => ShowShellPropertiesOnCurrentSta(resolvedSelection), cancellationToken).ConfigureAwait(false);
	}

	internal async Task<IReadOnlyList<WindowsShellAppExtensionCommand>> GetRegisteredCommandsAsync(IReadOnlyList<StorableReference> selection,
		IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> registrations, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(registrations);

		if (selection.Count is 0 || registrations.Count is 0 || await ResolveSelectionAsync(selection, cancellationToken).ConfigureAwait(false) is not { } resolvedSelection)
		{
			return [];
		}

		return await _source.Scheduler.InvokeOperationAsync(() => GetCommandsOnCurrentSta(resolvedSelection, registrations, cancellationToken), cancellationToken).ConfigureAwait(false);
	}

	private static IReadOnlyList<WindowsShellAppExtensionCommand> GetCommandsOnCurrentSta(WindowsShellResolvedSelection selection, CancellationToken cancellationToken)
	{
		var registrations = WindowsFileExplorerAppExtensionCatalog.GetRegistrations(selection.ItemTypes);

		return GetCommandsOnCurrentSta(selection, registrations, cancellationToken);
	}

	private static IReadOnlyList<WindowsShellAppExtensionCommand> GetCommandsOnCurrentSta(WindowsShellResolvedSelection selection,
		IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> registrations, CancellationToken cancellationToken)
	{
		if (registrations.Count is 0)
		{
			return [];
		}

		var shellItemArray = WindowsShellItemArrayFactory.Create(selection.Locators);
		var commands = new List<WindowsShellAppExtensionCommand>(registrations.Count);
		foreach (var registration in registrations)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var explorerCommand = TryCreateExplorerCommand(registration);
			if (explorerCommand is null)
			{
				continue;
			}

			var token = new WindowsShellAppExtensionCommandToken(registration.ClassId, registration.VerbId, []);
			if (TryCreateDescription(explorerCommand, shellItemArray, token, registration.DisplayName, depth: 0, cancellationToken) is { } description)
			{
				commands.Add(description);
			}
		}

		return commands;
	}

	private static bool InvokeOnCurrentSta(WindowsShellResolvedSelection selection, WindowsShellAppExtensionCommandToken token)
	{
		var registration = new WindowsFileExplorerAppExtensionRegistration(token.ClassId, token.VerbId, string.Empty, string.Empty);
		if (TryCreateExplorerCommand(registration) is not { } command)
		{
			return false;
		}

		foreach (var subCommandIndex in token.SubCommandPath)
		{
			if (!TryGetSubCommand(command, subCommandIndex, out command))
			{
				return false;
			}
		}

		var shellItemArray = WindowsShellItemArrayFactory.Create(selection.Locators);
		var invokeResult = command.Invoke(shellItemArray, null!);
		if (invokeResult != HRESULT.E_NOTIMPL)
		{
			return invokeResult.Succeeded;
		}

		return command is IObjectWithSelection objectWithSelection && command is IExecuteCommand executeCommand
			&& objectWithSelection.SetSelection(shellItemArray).Succeeded && executeCommand.Execute().Succeeded;
	}

	private static bool ShowShellPropertiesOnCurrentSta(WindowsShellResolvedSelection selection)
	{
		var shellItemArray = WindowsShellItemArrayFactory.Create(selection.Locators);
		var bindResult = shellItemArray.BindToHandler<IDataObject>(null, PInvoke.BHID_DataObject, out var dataObject);

		return bindResult.Succeeded && dataObject is not null && PInvoke.SHMultiFileProperties(dataObject, 0).Succeeded;
	}

	private static WindowsShellPropertySheetData ReadPropertySheetDataOnCurrentSta(
		WindowsShellResolvedSelection selection, IReadOnlyList<WindowsShellPropertyPage> pages, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var shellItemArray = WindowsShellItemArrayFactory.Create(selection.Locators);
		if (shellItemArray.GetItemAt(0, out var primaryItem).Failed || primaryItem is null)
		{
			return WindowsShellPropertySheetReader.CreateEmpty(pages);
		}

		return WindowsShellPropertySheetReader.Read(primaryItem, selection, pages, cancellationToken);
	}

	private (string? Description, ThumbnailResult? Icon) ReadGeneralProperties(IShellItem shellItem, WindowsItemLocator locator, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var description = ReadShellString(shellItem, "System.FileDescription");
		if (string.IsNullOrWhiteSpace(description) && ReadShellString(shellItem, "System.Link.TargetParsingPath") is { Length: > 0 } targetPath)
		{
			description = ReadFileDescription(targetPath);
		}

		if (string.IsNullOrWhiteSpace(description))
		{
			description = ReadShellString(shellItem, "System.Comment");
		}

		var payload = _thumbnailBackend.GetThumbnail(shellItem, locator, new ThumbnailRequest(48, ThumbnailMode.Icon), cancellationToken);
		var icon = payload is null ? null : new ThumbnailResult(payload.Content, payload.ContentType, payload.IsFallback);

		return (description, icon);
	}

	private static string? ReadFileDescription(string path)
	{
		try
		{
			return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static unsafe string? ReadShellString(IShellItem shellItem, string propertyId)
	{
		if (shellItem is not IShellItem2 shellItem2 || PInvoke.PSGetPropertyKeyFromName(propertyId, out var key).Failed || shellItem2.GetString(key, out var value).Failed)
		{
			return null;
		}

		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static unsafe WindowsShellAppExtensionCommand? TryCreateDescription(
		IExplorerCommand command,
		IShellItemArray selection,
		WindowsShellAppExtensionCommandToken token,
		string fallbackTitle,
		int depth,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var stateResult = command.GetState(selection, false, out var state);
		if (stateResult == HRESULT.E_NOTIMPL && command is IExplorerCommandState stateCommand)
		{
			stateResult = stateCommand.GetState(selection, false, out var stateValue);
			state = (_EXPCMDSTATE)stateValue;
		}

		if (stateResult.Value is PendingResult)
		{
			if (command is IExplorerCommandState slowStateCommand)
			{
				stateResult = slowStateCommand.GetState(selection, true, out var stateValue);
				state = (_EXPCMDSTATE)stateValue;
			}
			else
			{
				stateResult = command.GetState(selection, true, out state);
			}
		}
		else if (stateResult == HRESULT.E_NOTIMPL && WindowsShellCommandStorePropertyBag.TryCreate(token.VerbId) is not null)
		{
			stateResult = HRESULT.S_OK;
		}

		if (stateResult.Failed || state.HasFlag(_EXPCMDSTATE.ECS_HIDDEN))
		{
			return null;
		}

		var flagsResult = command.GetFlags(out var flags);
		if (flagsResult.Failed)
		{
			flags = _EXPCMDFLAGS.ECF_DEFAULT;
		}

		var title = ReadCommandString(command.GetTitle, selection);
		if (string.IsNullOrWhiteSpace(title))
		{
			title = string.IsNullOrWhiteSpace(fallbackTitle) ? token.VerbId : fallbackTitle;
		}

		var (iconPath, iconIndex) = ParseCommandIcon(ReadCommandString(command.GetIcon, selection));
		var children = depth < MaximumSubCommandDepth && flags.HasFlag(_EXPCMDFLAGS.ECF_HASSUBCOMMANDS) ? GetSubCommandDescriptions(command, selection, token, depth + 1, cancellationToken) : [];

		return new(
			token,
			token.VerbId,
			title,
			iconPath,
			iconIndex,
			!state.HasFlag(_EXPCMDSTATE.ECS_DISABLED),
			state.HasFlag(_EXPCMDSTATE.ECS_CHECKED),
			state.HasFlag(_EXPCMDSTATE.ECS_RADIOCHECK),
			flags.HasFlag(_EXPCMDFLAGS.ECF_ISSEPARATOR),
			children);
	}

	private static IReadOnlyList<WindowsShellAppExtensionCommand> GetSubCommandDescriptions(
		IExplorerCommand parent,
		IShellItemArray selection,
		WindowsShellAppExtensionCommandToken parentToken,
		int depth,
		CancellationToken cancellationToken)
	{
		if (parent.EnumSubCommands(out var enumerator).Failed || enumerator is null)
		{
			return [];
		}

		var descriptions = new List<WindowsShellAppExtensionCommand>();
		var subCommandIndex = 0;
		while (TryGetNextCommand(enumerator, out var subCommand))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var path = parentToken.SubCommandPath.Append(subCommandIndex).ToArray();
			var token = new WindowsShellAppExtensionCommandToken(parentToken.ClassId, parentToken.VerbId, path);
			if (TryCreateDescription(subCommand, selection, token, parentToken.VerbId, depth, cancellationToken) is { } description)
			{
				descriptions.Add(description);
			}

			subCommandIndex++;
		}

		return descriptions;
	}

	private static bool TryGetSubCommand(IExplorerCommand parent, int requestedIndex, out IExplorerCommand command)
	{
		command = null!;
		if (requestedIndex < 0 || parent.EnumSubCommands(out var enumerator).Failed || enumerator is null)
		{
			return false;
		}

		for (var index = 0; index <= requestedIndex; index++)
		{
			if (!TryGetNextCommand(enumerator, out command))
			{
				return false;
			}
		}

		return true;
	}

	private static unsafe bool TryGetNextCommand(IEnumExplorerCommand enumerator, out IExplorerCommand command)
	{
		var commands = new IExplorerCommand[1];
		uint fetched = 0;
		var result = enumerator.Next(1, commands, &fetched);
		command = commands[0];

		return result.Succeeded && fetched is 1 && command is not null;
	}

	private static unsafe IExplorerCommand? TryCreateExplorerCommand(WindowsFileExplorerAppExtensionRegistration registration)
	{
		var classId = registration.ClassId;
		var createResult = PInvoke.CoCreateInstance(classId, null, CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_LOCAL_SERVER, out IExplorerCommand? command);
		if (createResult.Failed || command is null)
		{
			return null;
		}

		if (command is IInitializeCommand initializer && !string.IsNullOrEmpty(registration.VerbId))
		{
			var propertyBag = WindowsShellCommandStorePropertyBag.TryCreate(registration.VerbId);
			fixed (char* verbId = registration.VerbId)
			{
				if (initializer.Initialize(new PCWSTR(verbId), propertyBag!).Failed)
				{
					return null;
				}
			}
		}

		return command;
	}

	private static unsafe string? ReadCommandString(CommandStringGetter getter, IShellItemArray selection)
	{
		PWSTR value = default;
		var result = getter(selection, &value);
		if (result.Failed || value.Value is null)
		{
			return null;
		}

		try
		{
			return new string(value.Value);
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static (string? Path, int Index) ParseCommandIcon(string? iconLocation)
	{
		if (string.IsNullOrWhiteSpace(iconLocation))
		{
			return (null, 0);
		}

		var path = iconLocation.Trim();
		var iconIndex = 0;
		var separatorIndex = path.LastIndexOf(',');
		if (separatorIndex >= 0 && int.TryParse(path.AsSpan(separatorIndex + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
		{
			iconIndex = parsedIndex;
			path = path[..separatorIndex];
		}

		path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

		return string.IsNullOrWhiteSpace(path) ? (null, 0) : (path, iconIndex);
	}

	private static bool HasCommonParent(IReadOnlyList<WindowsItemLocator> locators)
	{
		if (locators.Count is 0 || !TryGetParentPidl(locators[0].AbsolutePidl.Span, out var firstParent))
		{
			return false;
		}

		for (var index = 1; index < locators.Count; index++)
		{
			if (!TryGetParentPidl(locators[index].AbsolutePidl.Span, out var parent) || !firstParent.SequenceEqual(parent))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryGetParentPidl(ReadOnlySpan<byte> absolutePidl, out ReadOnlySpan<byte> parent)
	{
		parent = default;
		var offset = 0;
		var lastItemOffset = -1;
		while (offset + sizeof(ushort) <= absolutePidl.Length)
		{
			var itemSize = BinaryPrimitives.ReadUInt16LittleEndian(absolutePidl[offset..]);
			if (itemSize is 0)
			{
				if (lastItemOffset < 0 || offset + sizeof(ushort) != absolutePidl.Length)
				{
					return false;
				}

				parent = absolutePidl[..lastItemOffset];

				return true;
			}

			if (itemSize < sizeof(ushort) || offset + itemSize > absolutePidl.Length)
			{
				return false;
			}

			lastItemOffset = offset;
			offset += itemSize;
		}

		return false;
	}

	private async Task<WindowsShellResolvedSelection?> ResolveSelectionAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		var locators = new List<WindowsItemLocator>(selection.Count);
		var fileSystemPaths = new List<string>(selection.Count);
		WindowsStorable? firstItem = null;
		foreach (var reference in selection)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (reference.SourceId != _source.SourceId || await _source.ResolveAsync(reference, cancellationToken).ConfigureAwait(false) is not WindowsStorable item)
			{
				return null;
			}

			firstItem ??= item;
			locators.Add(item.Locator);
			if (item.FileSystemPath is { } fileSystemPath)
			{
				fileSystemPaths.Add(fileSystemPath);
			}
		}

		return firstItem is null ? null : new(locators, GetItemTypes(firstItem), fileSystemPaths, selection.Count is 1 && firstItem is WindowsFolder);
	}

	private static IReadOnlyList<string> GetItemTypes(WindowsStorable firstItem)
	{
		if (firstItem is WindowsFolder)
		{
			return ["Directory"];
		}

		var extension = Path.GetExtension(firstItem.Name);

		return string.IsNullOrEmpty(extension) ? ["*"] : [extension.ToLowerInvariant(), "*"];
	}

	private unsafe delegate HRESULT CommandStringGetter(IShellItemArray selection, PWSTR* value);
}

internal sealed record WindowsShellResolvedSelection(IReadOnlyList<WindowsItemLocator> Locators, IReadOnlyList<string> ItemTypes, IReadOnlyList<string> FileSystemPaths, bool IsSingleFolder);

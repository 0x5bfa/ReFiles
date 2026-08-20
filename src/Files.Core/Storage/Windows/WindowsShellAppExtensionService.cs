// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Enumerates and invokes packaged File Explorer app-extension commands without constructing an <c>IContextMenu</c>.
/// </summary>
public sealed class WindowsShellAppExtensionService
{
	private const int MaximumSubCommandDepth = 2;
	private const int PendingResult = unchecked((int)0x8000000A);
	private readonly WindowsStorageSource _source;

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

		return await _source.Scheduler.InvokeOperationAsync(() => GetCommandsOnCurrentSta(resolvedSelection), cancellationToken).ConfigureAwait(false);
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

	private static IReadOnlyList<WindowsShellAppExtensionCommand> GetCommandsOnCurrentSta(WindowsShellResolvedSelection selection)
	{
		var registrations = WindowsFileExplorerAppExtensionCatalog.GetRegistrations(selection.ItemTypes);
		if (registrations.Count is 0)
		{
			return [];
		}

		var shellItemArray = CreateShellItemArray(selection.Locators);
		var commands = new List<WindowsShellAppExtensionCommand>(registrations.Count);
		foreach (var registration in registrations)
		{
			var explorerCommand = TryCreateExplorerCommand(registration);
			if (explorerCommand is null)
			{
				continue;
			}

			var token = new WindowsShellAppExtensionCommandToken(registration.ClassId, registration.VerbId, []);
			if (TryCreateDescription(explorerCommand, shellItemArray, token, registration.DisplayName, depth: 0) is { } description)
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

		var shellItemArray = CreateShellItemArray(selection.Locators);

		return command.Invoke(shellItemArray, null!).Succeeded;
	}

	private static bool ShowShellPropertiesOnCurrentSta(WindowsShellResolvedSelection selection)
	{
		var shellItemArray = CreateShellItemArray(selection.Locators);
		var bindResult = shellItemArray.BindToHandler<IDataObject>(null, PInvoke.BHID_DataObject, out var dataObject);

		return bindResult.Succeeded && dataObject is not null && PInvoke.SHMultiFileProperties(dataObject, 0).Succeeded;
	}

	private static unsafe WindowsShellAppExtensionCommand? TryCreateDescription(
		IExplorerCommand command,
		IShellItemArray selection,
		WindowsShellAppExtensionCommandToken token,
		string fallbackTitle,
		int depth)
	{
		var stateResult = command.GetState(selection, false, out var state);
		if (stateResult.Value is PendingResult)
		{
			stateResult = command.GetState(selection, true, out state);
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

		var iconPath = ReadCommandString(command.GetIcon, selection);
		var children = depth < MaximumSubCommandDepth && flags.HasFlag(_EXPCMDFLAGS.ECF_HASSUBCOMMANDS) ? GetSubCommandDescriptions(command, selection, token, depth + 1) : [];

		return new(
			token,
			token.VerbId,
			title,
			iconPath,
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
		int depth)
	{
		if (parent.EnumSubCommands(out var enumerator).Failed || enumerator is null)
		{
			return [];
		}

		var descriptions = new List<WindowsShellAppExtensionCommand>();
		var subCommandIndex = 0;
		while (TryGetNextCommand(enumerator, out var subCommand))
		{
			var path = parentToken.SubCommandPath.Append(subCommandIndex).ToArray();
			var token = new WindowsShellAppExtensionCommandToken(parentToken.ClassId, parentToken.VerbId, path);
			if (TryCreateDescription(subCommand, selection, token, parentToken.VerbId, depth) is { } description)
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
			fixed (char* verbId = registration.VerbId)
			{
				if (initializer.Initialize(new PCWSTR(verbId), null!).Failed)
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

	private static unsafe IShellItemArray CreateShellItemArray(IReadOnlyList<WindowsItemLocator> locators)
	{
		if (locators.Count is 0)
		{
			throw new ArgumentException("A Shell selection cannot be empty.", nameof(locators));
		}

		var handles = new MemoryHandle[locators.Count];
		var itemIdLists = new nint[locators.Count];
		var pinnedCount = 0;
		try
		{
			for (var index = 0; index < locators.Count; index++)
			{
				if (locators[index].AbsolutePidl.IsEmpty)
				{
					throw new InvalidOperationException("A Windows Shell item does not have an absolute item ID list.");
				}

				handles[index] = locators[index].AbsolutePidl.Pin();
				itemIdLists[index] = (nint)handles[index].Pointer;
				pinnedCount++;
			}

			fixed (nint* itemIdListPointer = itemIdLists)
			{
				PInvoke.SHCreateShellItemArrayFromIDLists(checked((uint)itemIdLists.Length), (ITEMIDLIST**)itemIdListPointer, out var selection).ThrowOnFailure();

				return selection;
			}
		}
		finally
		{
			for (var index = 0; index < pinnedCount; index++)
			{
				handles[index].Dispose();
			}
		}
	}

	private async Task<WindowsShellResolvedSelection?> ResolveSelectionAsync(IReadOnlyList<StorableReference> selection, CancellationToken cancellationToken)
	{
		var locators = new List<WindowsItemLocator>(selection.Count);
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
		}

		return firstItem is null ? null : new(locators, GetItemTypes(firstItem));
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

internal sealed record WindowsShellResolvedSelection(IReadOnlyList<WindowsItemLocator> Locators, IReadOnlyList<string> ItemTypes);

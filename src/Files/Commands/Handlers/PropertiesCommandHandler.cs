// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ItemProperties;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Files.Commands.Handlers;

internal sealed class PropertiesCommandHandler : ICommandHandler
{
	private readonly IItemPropertiesService? _propertiesService;

	public CommandId Id => CommandIds.Properties;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.AllowParallel;

	public CommandStateInvalidation StateDependencies => CommandStateInvalidation.Selection | CommandStateInvalidation.Loading;

	internal PropertiesCommandHandler(IItemPropertiesService? propertiesService)
	{
		_propertiesService = propertiesService;
	}

	public CommandState GetState(CommandContext context)
	{
		var isEnabled = context.ActiveFolderBrowser is { IsLoading: false, SelectedItems.Count: > 0 };

		return new(true, isEnabled);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { SelectedItems.Count: > 0 } browser)
		{
			return CommandExecutionResult.Unsupported();
		}

		var selection = browser.SelectedItems.ToArray();
		var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
		if (shiftState.HasFlag(CoreVirtualKeyStates.Down))
		{
			return await browser.ShowShellPropertiesAsync(selection, cancellationToken).ConfigureAwait(false) ? CommandExecutionResult.Succeeded() : CommandExecutionResult.Unsupported();
		}

		if (_propertiesService is null)
		{
			return CommandExecutionResult.Unsupported();
		}

		await _propertiesService.ShowAsync(selection);

		return CommandExecutionResult.Succeeded();
	}
}

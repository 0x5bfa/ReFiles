// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Commands.Handlers;

internal sealed class SelectionCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandStateInvalidation StateDependencies =>
		CommandStateInvalidation.ActiveTab |
		CommandStateInvalidation.Selection |
		CommandStateInvalidation.Loading |
		CommandStateInvalidation.Location;

	public CommandState GetState(CommandContext context)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return new(false, false);
		}

		var isEnabled = id switch
		{
			var commandId when commandId == CommandIds.SelectAll => browser.CanSelectAllItems,
			var commandId when commandId == CommandIds.InvertSelection => browser.CanInvertItemSelection,
			var commandId when commandId == CommandIds.ClearSelection => browser.CanClearItemSelection,
			_ => false,
		};

		return new(browser.SupportsItemSelection, isEnabled);
	}

	public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return ValueTask.FromResult(CommandExecutionResult.Unsupported());
		}

		switch (id)
		{
			case var commandId when commandId == CommandIds.SelectAll:
				browser.SelectAllItems();
				break;
			case var commandId when commandId == CommandIds.InvertSelection:
				browser.InvertItemSelection();
				break;
			case var commandId when commandId == CommandIds.ClearSelection:
				browser.ClearItemSelection();
				break;
			default:
				throw new InvalidOperationException($"Unsupported selection command '{id}'.");
		}

		return ValueTask.FromResult(CommandExecutionResult.Succeeded());
	}
}

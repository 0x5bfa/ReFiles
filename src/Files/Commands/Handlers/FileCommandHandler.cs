// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.Commands.Handlers;

internal sealed class FileCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandStateInvalidation StateDependencies =>
		CommandStateInvalidation.Selection |
		CommandStateInvalidation.Loading |
		CommandStateInvalidation.Location |
		CommandStateInvalidation.Clipboard;

	public CommandState GetState(CommandContext context)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return new(false, false);
		}

		var isEnabled = id switch
		{
			var commandId when commandId == CommandIds.Copy => browser.CanCopy,
			var commandId when commandId == CommandIds.Cut => browser.CanCut,
			var commandId when commandId == CommandIds.Paste => browser.CanPaste,
			var commandId when commandId == CommandIds.Delete => browser.CanDelete,
			_ => false,
		};

		return new(true, isEnabled);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser)
		{
			return CommandExecutionResult.Unsupported();
		}

		switch (id)
		{
			case var commandId when commandId == CommandIds.Copy:
				await browser.CopySelectionAsync(move: false, cancellationToken: cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.Cut:
				await browser.CopySelectionAsync(move: true, cancellationToken: cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.Paste:
				await browser.PasteFromClipboardAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.Delete:
				await browser.DeleteSelectionAsync(cancellationToken).ConfigureAwait(false);
				break;
			default:
				throw new InvalidOperationException($"Unsupported file command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}
}

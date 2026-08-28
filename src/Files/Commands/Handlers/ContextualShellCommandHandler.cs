// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands.Handlers;

internal sealed class ContextualShellCommandHandler(CommandId id, string shellCommandId) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandStateInvalidation StateDependencies => CommandStateInvalidation.Selection | CommandStateInvalidation.Loading |
		CommandStateInvalidation.Location | CommandStateInvalidation.ContextualCommands;

	public CommandState GetState(CommandContext context)
	{
		return context.ActiveFolderBrowser?.GetContextualCommandState(shellCommandId) ?? new(false, false);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveFolderBrowser is not { } browser || !await browser.InvokeContextualCommandAsync(shellCommandId, cancellationToken).ConfigureAwait(false))
		{
			return CommandExecutionResult.Unsupported();
		}

		return CommandExecutionResult.Succeeded();
	}
}

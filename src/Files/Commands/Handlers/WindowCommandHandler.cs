// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands.Handlers;

internal sealed class WindowCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy =>
		CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandState GetState(CommandContext context)
	{
		var isEnabled = id == CommandIds.NewTab
			|| (id == CommandIds.CloseTab
				&& context.InvokedTab is not null
				&& context.Root.Tabs.Count > 1);
		return new(true, isEnabled);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(
		CommandContext context,
		CancellationToken cancellationToken = default)
	{
		switch (id)
		{
			case var commandId when commandId == CommandIds.NewTab:
				await context.Root.OpenTabAsync(cancellationToken)
					.ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.CloseTab:
				if (context.InvokedTab is not { } tab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.CloseTabAsync(
					tab.Id,
					cancellationToken).ConfigureAwait(false);
				break;
			default:
				throw new InvalidOperationException(
					$"Unsupported window command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}
}

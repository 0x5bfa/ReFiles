// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.AppModels;

namespace Files.Commands.Handlers;

internal sealed class PaneCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy =>
		CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandState GetState(CommandContext context)
	{
		var tab = context.ActiveTab;
		var isEnabled = id == CommandIds.NewPane
			? tab is not null
			: tab?.CanClosePane is true;

		return new(true, isEnabled);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		if (context.ActiveTab is not { } tab)
		{
			return CommandExecutionResult.Unsupported();
		}

		switch (id)
		{
			case var commandId when commandId == CommandIds.NewPane:
				var orientation = tab.SplitOrientation is
					PaneSplitOrientation.Horizontal
					? PaneSplitOrientation.Vertical
					: PaneSplitOrientation.Horizontal;
				await tab.OpenPaneAsync(orientation, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.ClosePane:
				await tab.CloseActivePaneAsync(cancellationToken)
					.ConfigureAwait(false);
				break;
			default:
				throw new InvalidOperationException($"Unsupported pane command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}
}

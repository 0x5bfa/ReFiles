// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Sessions;
using Files.ViewModels;

namespace Files.Commands.Handlers;

internal sealed class PaneCommandHandler(CommandId id) : ICommandHandler
{
	public CommandId Id => id;

	public CommandConcurrencyPolicy ConcurrencyPolicy =>
		CommandConcurrencyPolicy.RejectWhileRunning;

	public CommandState GetState(CommandContext context)
	{
		var tab = context.ActiveTab;
		var isEnabled = id switch
		{
			var commandId when commandId == CommandIds.NewPane => tab?.CanOpenPane is true,
			var commandId when commandId == CommandIds.ClosePane => tab?.CanClosePane is true,
			var commandId when commandId == CommandIds.SplitPaneVertical => tab?.CanSplitPane(PaneSplitOrientation.Vertical) is true,
			var commandId when commandId == CommandIds.SplitPaneHorizontal => tab?.CanSplitPane(PaneSplitOrientation.Horizontal) is true,
			_ => false,
		};

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
			case var commandId when commandId == CommandIds.SplitPaneVertical:
				await SplitPaneAsync(tab, PaneSplitOrientation.Vertical, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.SplitPaneHorizontal:
				await SplitPaneAsync(tab, PaneSplitOrientation.Horizontal, cancellationToken).ConfigureAwait(false);
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

	private static async Task SplitPaneAsync(TabViewModel tab, PaneSplitOrientation orientation, CancellationToken cancellationToken)
	{
		if (tab.Panes.Count is 2)
		{
			tab.SetSplitOrientation(orientation);

			return;
		}

		await tab.OpenPaneAsync(orientation, cancellationToken).ConfigureAwait(false);
	}
}

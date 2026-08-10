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
		var invokedTabIndex = context.InvokedTab is { } invokedTab
			? context.Root.GetTabIndex(invokedTab.Id)
			: -1;
		var hasInvokedTab = invokedTabIndex >= 0;
		var isEnabled = id switch
		{
			var commandId when commandId == CommandIds.NewTab => true,
			var commandId when commandId == CommandIds.DuplicateTab => hasInvokedTab,
			var commandId when commandId == CommandIds.CloseTab => hasInvokedTab,
			var commandId when commandId == CommandIds.CloseTabsToLeft => invokedTabIndex > 0,
			var commandId when commandId == CommandIds.CloseTabsToRight => hasInvokedTab && invokedTabIndex < context.Root.Tabs.Count - 1,
			var commandId when commandId == CommandIds.CloseOtherTabs => hasInvokedTab && context.Root.Tabs.Count > 1,
			var commandId when commandId == CommandIds.ReopenTab => hasInvokedTab && context.Root.CanReopenTab,
			_ => false,
		};

		return new(true, isEnabled);
	}

	public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		switch (id)
		{
			case var commandId when commandId == CommandIds.NewTab:
				await context.Root.OpenTabAsync(cancellationToken)
					.ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.DuplicateTab:
				if (context.InvokedTab is not { } duplicateTab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.DuplicateTabAsync(duplicateTab.Id, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.CloseTabsToLeft:
				if (context.InvokedTab is not { } leftTab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.CloseTabsToLeftAsync(leftTab.Id, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.CloseTabsToRight:
				if (context.InvokedTab is not { } rightTab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.CloseTabsToRightAsync(rightTab.Id, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.CloseOtherTabs:
				if (context.InvokedTab is not { } otherTabsTab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.CloseOtherTabsAsync(otherTabsTab.Id, cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.ReopenTab:
				await context.Root.ReopenTabAsync(cancellationToken).ConfigureAwait(false);
				break;
			case var commandId when commandId == CommandIds.MoveTabToNewWindow:
				return CommandExecutionResult.Unsupported();
			case var commandId when commandId == CommandIds.CloseTab:
				if (context.InvokedTab is not { } tab)
				{
					return CommandExecutionResult.Unsupported();
				}

				await context.Root.CloseTabAsync(tab.Id, cancellationToken).ConfigureAwait(false);
				break;
			default:
				throw new InvalidOperationException($"Unsupported window command '{id}'.");
		}

		return CommandExecutionResult.Succeeded();
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Controls;

namespace Files.Commands.Handlers;

internal sealed class SidebarCommandHandler : ICommandHandler
{
	public CommandId Id => CommandIds.ToggleSidebar;

	public CommandConcurrencyPolicy ConcurrencyPolicy => CommandConcurrencyPolicy.AllowParallel;

	public CommandStateInvalidation StateDependencies => CommandStateInvalidation.Pane;

	public CommandState GetState(CommandContext context)
	{
		var isCompact = context.Root.SidebarDisplayMode is SidebarDisplayMode.Compact;

		return new(isCompact, true, !isCompact);
	}

	public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default)
	{
		context.Root.SidebarDisplayMode = context.Root.SidebarDisplayMode is SidebarDisplayMode.Compact ? SidebarDisplayMode.Expanded : SidebarDisplayMode.Compact;

		return ValueTask.FromResult(CommandExecutionResult.Succeeded());
	}
}

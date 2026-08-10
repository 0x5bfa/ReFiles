// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

[Flags]
public enum CommandStateInvalidation
{
	None = 0,
	Selection = 1 << 0,
	Loading = 1 << 1,
	Location = 1 << 2,
	Navigation = 1 << 3,
	ViewSettings = 1 << 4,
	ActiveTab = 1 << 5,
	Pane = 1 << 6,
	Clipboard = 1 << 7,
	All = int.MaxValue,
}

public interface ICommandHandler
{
	CommandId Id { get; }

	CommandConcurrencyPolicy ConcurrencyPolicy { get; }

	CommandStateInvalidation StateDependencies { get; }

	CommandState GetState(CommandContext context);

	ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken = default);
}

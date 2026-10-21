// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Commands;

public enum CommandExecutionStatus
{
	Succeeded,
	Canceled,
	Unsupported,
	Failed,
}

public sealed record CommandExecutionResult(CommandExecutionStatus Status, Exception? Error = null)
{
	public static CommandExecutionResult Succeeded()
	{
		return new(CommandExecutionStatus.Succeeded);
	}

	public static CommandExecutionResult Canceled()
	{
		return new(CommandExecutionStatus.Canceled);
	}

	public static CommandExecutionResult Unsupported()
	{
		return new(CommandExecutionStatus.Unsupported);
	}

	public static CommandExecutionResult Failed(Exception error)
	{
		ArgumentNullException.ThrowIfNull(error);

		return new(CommandExecutionStatus.Failed, error);
	}
}

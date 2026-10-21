// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

/// <summary>Describes the acknowledged state of a server operation.</summary>
public enum OperationState
{
	/// <summary>The operation has been accepted.</summary>
	Queued,
	/// <summary>The operation is executing.</summary>
	Running,
	/// <summary>The operation completed successfully.</summary>
	Succeeded,
	/// <summary>The operation failed.</summary>
	Failed,
	/// <summary>The operation acknowledged cancellation and stopped.</summary>
	Canceled,
	Pausing,
	Paused,
	Resuming,
	WaitingForUser,
}

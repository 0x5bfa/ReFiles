// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Operations;

/// <summary>Defines a complete operation owned by the server, including any item loop.</summary>
internal interface IServerOperation
{
	Guid Id { get; }
	string Kind { get; }
	int ItemCount { get; }
	string? DestinationPath { get; }
	bool CanPause { get; }
	Task ExecuteAsync(OperationExecutionContext context, CancellationToken cancellationToken);
}

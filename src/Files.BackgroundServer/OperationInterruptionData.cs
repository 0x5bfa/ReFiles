// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;

namespace Files.BackgroundServer;

/// <summary>Transfers a specific pending decision and its supported responses.</summary>
public struct OperationInterruptionData
{
	public Guid Id;
	public int Kind;
	public uint AvailableResponses;
	public int ErrorCode;
	public string ItemName;
	public string DestinationPath;
	public bool CanApplyToAll;
}

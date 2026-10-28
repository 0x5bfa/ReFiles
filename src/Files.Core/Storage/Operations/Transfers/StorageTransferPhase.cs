// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Identifies the last transfer phase reached.</summary>
public enum StorageTransferPhase
{
	/// <summary>The input is being resolved or read.</summary>
	Reading,
	/// <summary>The output is being flushed and closed.</summary>
	Finalizing,
	/// <summary>The staged item is being published.</summary>
	Publishing,
	/// <summary>The destination publication was acknowledged.</summary>
	Completed,
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Describes an observed operation state.</summary>
public enum StorageJobState
{
	/// <summary>The job is queued or running.</summary>
	Running,
	/// <summary>A pause has been requested.</summary>
	Pausing,
	/// <summary>The job has acknowledged a pause.</summary>
	Paused,
	/// <summary>A resume has been requested.</summary>
	Resuming,
	/// <summary>The job requires a user decision.</summary>
	WaitingForUser,
	/// <summary>The job completed successfully.</summary>
	Succeeded,
	/// <summary>The job failed.</summary>
	Failed,
	/// <summary>The job acknowledged cancellation.</summary>
	Canceled,
}

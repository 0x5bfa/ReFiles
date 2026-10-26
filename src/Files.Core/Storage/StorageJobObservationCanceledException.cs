// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Reports cancellation of observation while the accepted job continues independently.</summary>
public sealed class StorageJobObservationCanceledException : OperationCanceledException
{
	/// <summary>Gets the accepted job that can be observed again.</summary>
	public StorageJobHandle Job { get; }

	/// <summary>Creates a canceled observation.</summary>
	/// <param name="job">The accepted job.</param>
	/// <param name="cancellationToken">The canceled observation token.</param>
	public StorageJobObservationCanceledException(StorageJobHandle job, CancellationToken cancellationToken) : base("Job observation was canceled; the job was not canceled.", cancellationToken)
	{
		Job = job;
	}
}

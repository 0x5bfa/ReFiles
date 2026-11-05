// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Reports uncertain acceptance or completion without implying that retry is safe.</summary>
public sealed class StorageJobUnknownException : Exception
{
	/// <summary>Gets the identity to retain for subsequent observation.</summary>
	public StorageJobHandle Job { get; }

	/// <summary>Gets whether the server acknowledged acceptance before the failure.</summary>
	public bool Accepted { get; }

	/// <summary>Creates an uncertain job outcome.</summary>
	/// <param name="job">The attempted or accepted job identity.</param>
	/// <param name="accepted">Whether acceptance was acknowledged.</param>
	/// <param name="message">The failure description.</param>
	/// <param name="innerException">The optional transport failure.</param>
	public StorageJobUnknownException(StorageJobHandle job, bool accepted, string message, Exception? innerException = null) : base(message, innerException)
	{
		Job = job;
		Accepted = accepted;
	}
}

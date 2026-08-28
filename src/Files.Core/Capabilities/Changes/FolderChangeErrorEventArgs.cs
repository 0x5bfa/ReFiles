// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Changes;

/// <summary>
/// Provides an error raised by a folder change notification pump.
/// </summary>
public sealed class FolderChangeErrorEventArgs : EventArgs
{
	/// <summary>Gets the error raised by the change source.</summary>
	public Exception Error { get; }

	/// <summary>Initializes error event data.</summary>
	/// <param name="error">The error raised by the change source.</param>
	public FolderChangeErrorEventArgs(Exception error)
	{
		Error = error ?? throw new ArgumentNullException(nameof(error));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Provides an error raised by a folder change notification pump.
/// </summary>
public sealed class FolderChangeErrorEventArgs : EventArgs
{
	public Exception Error { get; }

	public FolderChangeErrorEventArgs(Exception error)
	{
		Error = error ?? throw new ArgumentNullException(nameof(error));
	}
}

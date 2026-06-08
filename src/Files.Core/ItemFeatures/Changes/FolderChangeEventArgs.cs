// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Provides a managed folder change to an event subscriber.
/// </summary>
public sealed class FolderChangeEventArgs : EventArgs
{
	public FolderChangeEventArgs(FolderChange change)
	{
		Change = change ?? throw new ArgumentNullException(nameof(change));
	}

	public FolderChange Change { get; }
}

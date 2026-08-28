// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Changes;

/// <summary>
/// Provides a managed folder change to an event subscriber.
/// </summary>
public sealed class FolderChangeEventArgs : EventArgs
{
	/// <summary>Gets the folder change.</summary>
	public FolderChange Change { get; }

	/// <summary>Initializes folder change event data.</summary>
	/// <param name="change">The folder change.</param>
	public FolderChangeEventArgs(FolderChange change)
	{
		Change = change ?? throw new ArgumentNullException(nameof(change));
	}
}

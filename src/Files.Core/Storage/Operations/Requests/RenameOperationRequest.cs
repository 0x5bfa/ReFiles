// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Requests that one item be renamed within its current parent.
/// </summary>
public sealed record RenameOperationRequest : StorageOperationRequest
{
	/// <summary>Gets the item to rename.</summary>
	public StorableReference Item { get; }

	/// <summary>Gets the new item name.</summary>
	public string NewName { get; }

	/// <summary>Initializes a rename request.</summary>
	/// <param name="item">The item to rename.</param>
	/// <param name="newName">The new item name.</param>
	public RenameOperationRequest(StorableReference item, string newName)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		Item = item;
		NewName = newName;
	}
}

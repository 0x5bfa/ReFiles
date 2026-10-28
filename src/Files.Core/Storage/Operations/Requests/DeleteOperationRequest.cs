// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Requests that one item be deleted or moved to the Recycle Bin.
/// </summary>
public sealed record DeleteOperationRequest : StorageOperationRequest
{
	/// <summary>Gets the item to delete.</summary>
	public StorableReference Item { get; }

	/// <summary>Gets a value indicating whether the item should bypass the Recycle Bin.</summary>
	public bool Permanently { get; }

	/// <summary>Initializes a delete request.</summary>
	/// <param name="item">The item to delete.</param>
	/// <param name="permanently">Whether to delete the item permanently.</param>
	public DeleteOperationRequest(StorableReference item, bool permanently = false)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
		Permanently = permanently;
	}
}

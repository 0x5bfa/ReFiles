// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Describes whether a source can recycle an item instead of deleting it permanently.</summary>
public interface IStorageDeletionPolicy
{
	/// <summary>Determines whether the item can be recycled.</summary>
	/// <param name="reference">The item to inspect.</param>
	/// <returns>Whether a nonpermanent delete can be requested.</returns>
	bool CanRecycle(StorableReference reference);
}

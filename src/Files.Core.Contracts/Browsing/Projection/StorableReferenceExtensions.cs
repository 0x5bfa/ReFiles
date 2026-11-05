// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>Provides helpers for converting storage references to browse keys.</summary>
public static class StorableReferenceExtensions
{
	/// <summary>Gets the browse key for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <returns>The key identifying the referenced item.</returns>
	public static StorableKey GetKey(this StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);

		return new StorableKey(reference.SourceId, reference.ItemId);
	}
}

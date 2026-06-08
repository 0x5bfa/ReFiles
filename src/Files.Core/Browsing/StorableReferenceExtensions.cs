// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Browsing;

public static class StorableReferenceExtensions
{
	public static StorableKey GetKey(this StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);
		return new StorableKey(reference.SourceId, reference.ItemId);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Archives;

/// <summary>
/// Marks a storage item as a candidate for archive-backed navigation.
/// </summary>
public interface IArchiveSource
{
	/// <summary>Gets the archive reference represented by the item.</summary>
	StorableReference Archive { get; }
}

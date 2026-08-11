// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Supplies a resolvable address without also claiming to be a storage item.
/// </summary>
public interface IStorageAddressSource
{
	/// <summary>Gets the address that can be resolved by a storage source.</summary>
	StorageAddress Address { get; }
}

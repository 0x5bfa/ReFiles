// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Supplies a resolvable address without also claiming to be a storage item.
/// </summary>
public interface IStorageAddressSource
{
	StorageAddress Address { get; }
}

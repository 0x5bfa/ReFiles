// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Indicates that a resolved navigation address identifies a nonfolder item.</summary>
public sealed class StorageItemNotFolderException : InvalidOperationException
{
	/// <summary>Gets the address that was resolved.</summary>
	public StorageAddress Address { get; }

	/// <summary>Initializes a nonfolder navigation failure.</summary>
	/// <param name="address">The resolved address.</param>
	public StorageItemNotFolderException(StorageAddress address) : base("The storage address does not identify a folder.")
	{
		ArgumentNullException.ThrowIfNull(address);

		Address = address;
	}
}

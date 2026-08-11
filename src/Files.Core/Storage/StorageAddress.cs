// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Describes an address that a storage source may be able to resolve.
/// </summary>
public sealed record StorageAddress
{
	/// <summary>Gets the address scheme.</summary>
	public string Scheme { get; }

	/// <summary>Gets the scheme-specific address value.</summary>
	public string Value { get; }

	/// <summary>Initializes a storage address.</summary>
	/// <param name="scheme">The address scheme.</param>
	/// <param name="value">The scheme-specific value.</param>
	public StorageAddress(string scheme, string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Scheme = scheme;
		Value = value;
	}

	/// <summary>Returns the address in scheme/value form.</summary>
	/// <returns>The formatted storage address.</returns>
	public override string ToString() => $"{Scheme}:{Value}";
}

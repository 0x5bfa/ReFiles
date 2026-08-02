// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Describes an address that a storage source may be able to resolve.
/// </summary>
public sealed record StorageAddress
{
	public string Scheme { get; }

	public string Value { get; }

	public StorageAddress(string scheme, string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Scheme = scheme;
		Value = value;
	}

	public override string ToString() => $"{Scheme}:{Value}";
}

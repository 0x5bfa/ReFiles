// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Identifies a configured storage source, such as a Windows shell namespace or FTP connection.
/// </summary>
public sealed record StorageSourceId
{
	/// <summary>Gets the stable source identifier value.</summary>
	public string Value { get; }

	/// <summary>Initializes a storage source identifier.</summary>
	/// <param name="value">The identifier value.</param>
	public StorageSourceId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Value = value;
	}

	/// <summary>Returns the identifier value.</summary>
	/// <returns>The identifier value.</returns>
	public override string ToString() => Value;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Contains a password used to open an archive.</summary>
public sealed class ArchiveCredential
{
	/// <summary>Gets the archive password.</summary>
	public string Password { get; }

	/// <summary>Initializes an archive credential.</summary>
	/// <param name="password">The archive password.</param>
	public ArchiveCredential(string password)
	{
		ArgumentNullException.ThrowIfNull(password);

		Password = password;
	}

	/// <summary>Returns a non-sensitive credential description.</summary>
	/// <returns>The credential type name.</returns>
	public override string ToString()
		=> nameof(ArchiveCredential);
}

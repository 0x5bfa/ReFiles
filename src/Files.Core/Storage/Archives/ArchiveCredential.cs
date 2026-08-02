// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public sealed class ArchiveCredential
{
	public string Password { get; }

	public ArchiveCredential(string password)
	{
		ArgumentNullException.ThrowIfNull(password);

		Password = password;
	}

	public override string ToString()
		=> nameof(ArchiveCredential);
}

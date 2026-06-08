// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public sealed record ArchiveCredentialChallenge
{
	public ArchiveCredentialChallenge(
		StorableReference archive,
		string displayName,
		int attempt,
		bool previousCredentialRejected)
	{
		ArgumentNullException.ThrowIfNull(archive);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
		if (attempt < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(attempt),
				attempt,
				"Archive credential attempts are one-based.");
		}

		Archive = archive;
		DisplayName = displayName;
		Attempt = attempt;
		PreviousCredentialRejected = previousCredentialRejected;
	}

	public StorableReference Archive { get; }

	public string DisplayName { get; }

	public int Attempt { get; }

	public bool PreviousCredentialRejected { get; }
}

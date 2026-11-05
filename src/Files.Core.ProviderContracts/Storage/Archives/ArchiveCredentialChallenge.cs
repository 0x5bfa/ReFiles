// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Describes a request for archive credentials.</summary>
public sealed record ArchiveCredentialChallenge
{
	/// <summary>Gets the archive reference.</summary>
	public StorableReference Archive { get; }

	/// <summary>Gets the display name shown to the user.</summary>
	public string DisplayName { get; }

	/// <summary>Gets the one-based credential attempt number.</summary>
	public int Attempt { get; }

	/// <summary>Gets a value indicating whether the previous credential was rejected.</summary>
	public bool PreviousCredentialRejected { get; }

	/// <summary>Initializes an archive credential challenge.</summary>
	/// <param name="archive">The archive reference.</param>
	/// <param name="displayName">The display name of the archive.</param>
	/// <param name="attempt">The one-based attempt number.</param>
	/// <param name="previousCredentialRejected">Whether the previous credential was rejected.</param>
	public ArchiveCredentialChallenge(StorableReference archive, string displayName, int attempt, bool previousCredentialRejected)
	{
		ArgumentNullException.ThrowIfNull(archive);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		if (attempt < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Archive credential attempts are one-based.");
		}

		Archive = archive;
		DisplayName = displayName;
		Attempt = attempt;
		PreviousCredentialRejected = previousCredentialRejected;
	}
}

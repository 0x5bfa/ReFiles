// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Indicates that an archive requires credentials before it can be opened.</summary>
public sealed class ArchiveCredentialRequiredException : Exception
{
	/// <summary>Gets the credential challenge.</summary>
	public ArchiveCredentialChallenge Challenge { get; }

	/// <summary>Initializes a credential-required exception.</summary>
	/// <param name="challenge">The credential challenge.</param>
	public ArchiveCredentialRequiredException(ArchiveCredentialChallenge challenge)
		: base(CreateMessage(challenge))
	{
		Challenge = challenge;
	}

	private static string CreateMessage(ArchiveCredentialChallenge challenge)
	{
		ArgumentNullException.ThrowIfNull(challenge);

		return $"A credential is required to open archive '{challenge.DisplayName}'.";
	}
}

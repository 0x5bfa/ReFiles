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
	/// <param name="innerException">The optional read failure retained for diagnostics.</param>
	public ArchiveCredentialRequiredException(ArchiveCredentialChallenge challenge, Exception? innerException = null)
		: base(CreateMessage(challenge), innerException)
	{
		Challenge = challenge;
	}

	private static string CreateMessage(ArchiveCredentialChallenge challenge)
	{
		ArgumentNullException.ThrowIfNull(challenge);

		return $"A credential is required to open archive '{challenge.DisplayName}'.";
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public sealed class ArchiveCredentialRequiredException : Exception
{
	public ArchiveCredentialChallenge Challenge { get; }

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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public enum ArchiveProbeKind
{
	Unknown,
	Unencrypted,
	Encrypted,
	CredentialRequired,
}

public sealed record ArchiveProbeResult
{
	public ArchiveProbeKind Kind { get; }

	public ArchiveCredentialChallenge? Challenge { get; }

	public static ArchiveProbeResult Unknown { get; } =
		new(ArchiveProbeKind.Unknown, null);

	public static ArchiveProbeResult Unencrypted { get; } =
		new(ArchiveProbeKind.Unencrypted, null);

	public static ArchiveProbeResult Encrypted { get; } =
		new(ArchiveProbeKind.Encrypted, null);

	private ArchiveProbeResult(ArchiveProbeKind kind, ArchiveCredentialChallenge? challenge)
	{
		Kind = kind;
		Challenge = challenge;
	}

	public static ArchiveProbeResult CredentialRequired(ArchiveCredentialChallenge challenge)
	{
		ArgumentNullException.ThrowIfNull(challenge);

		return new ArchiveProbeResult(ArchiveProbeKind.CredentialRequired, challenge);
	}
}

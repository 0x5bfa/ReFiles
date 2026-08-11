// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Describes what an archive probe discovered.</summary>
public enum ArchiveProbeKind
{
	/// <summary>The archive format could not be determined.</summary>
	Unknown,
	/// <summary>The archive is not encrypted.</summary>
	Unencrypted,
	/// <summary>The archive is encrypted.</summary>
	Encrypted,
	/// <summary>The archive requires credentials.</summary>
	CredentialRequired,
}

/// <summary>Contains the result of probing an archive.</summary>
public sealed record ArchiveProbeResult
{
	/// <summary>Gets the probe classification.</summary>
	public ArchiveProbeKind Kind { get; }

	/// <summary>Gets the credential challenge, when credentials are required.</summary>
	public ArchiveCredentialChallenge? Challenge { get; }

	/// <summary>Gets an unknown probe result.</summary>
	public static ArchiveProbeResult Unknown { get; } =
		new(ArchiveProbeKind.Unknown, null);

	/// <summary>Gets an unencrypted probe result.</summary>
	public static ArchiveProbeResult Unencrypted { get; } =
		new(ArchiveProbeKind.Unencrypted, null);

	/// <summary>Gets an encrypted probe result.</summary>
	public static ArchiveProbeResult Encrypted { get; } =
		new(ArchiveProbeKind.Encrypted, null);

	private ArchiveProbeResult(ArchiveProbeKind kind, ArchiveCredentialChallenge? challenge)
	{
		Kind = kind;
		Challenge = challenge;
	}

	/// <summary>Creates a credential-required probe result.</summary>
	/// <param name="challenge">The credential challenge.</param>
	/// <returns>The credential-required result.</returns>
	public static ArchiveProbeResult CredentialRequired(ArchiveCredentialChallenge challenge)
	{
		ArgumentNullException.ThrowIfNull(challenge);

		return new ArchiveProbeResult(ArchiveProbeKind.CredentialRequired, challenge);
	}
}

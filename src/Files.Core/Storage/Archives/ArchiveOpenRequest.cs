// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Archives;

/// <summary>Supplies borrowed, seekable archive input to a reader without application or browse models.</summary>
public sealed record ArchiveOpenRequest
{
	/// <summary>Gets the container reference for diagnostics and credential challenges.</summary>
	public StorableReference Archive { get; }

	/// <summary>Gets the container display name.</summary>
	public string Name { get; }

	/// <summary>Gets the borrowed input. Readers must leave it open on every outcome.</summary>
	public Stream Input { get; }

	/// <summary>Gets the optional credential for this attempt.</summary>
	public ArchiveCredential? Credential { get; }

	/// <summary>Gets the credential attempt number.</summary>
	public int CredentialAttempt { get; }

	/// <summary>Creates a reader request.</summary>
	/// <param name="archive">The container reference.</param>
	/// <param name="name">The container name.</param>
	/// <param name="input">The borrowed seekable input.</param>
	/// <param name="credential">The optional credential.</param>
	/// <param name="credentialAttempt">The credential attempt number.</param>
	public ArchiveOpenRequest(StorableReference archive, string name, Stream input, ArchiveCredential? credential = null, int credentialAttempt = 0)
	{
		ArgumentNullException.ThrowIfNull(archive);

		ArgumentNullException.ThrowIfNull(name);

		ArgumentNullException.ThrowIfNull(input);

		ArgumentOutOfRangeException.ThrowIfNegative(credentialAttempt);

		if (!input.CanRead || !input.CanSeek)
		{
			throw new ArgumentException("Archive input must be readable and seekable.", nameof(input));
		}

		Archive = archive;
		Name = name;
		Input = input;
		Credential = credential;
		CredentialAttempt = credentialAttempt;
	}
}

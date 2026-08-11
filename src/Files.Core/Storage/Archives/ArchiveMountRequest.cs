// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Storage.Archives;

/// <summary>Describes an archive mount attempt.</summary>
public sealed record ArchiveMountRequest
{
	/// <summary>Gets the storage source that owns the archive.</summary>
	public IStorageSource Source { get; }

	/// <summary>Gets the archive item model.</summary>
	public IStorableModel ArchiveModel { get; }

	/// <summary>Gets the stable archive reference.</summary>
	public StorableReference Archive => ArchiveModel.Reference;

	/// <summary>Gets the credential supplied for this attempt.</summary>
	public ArchiveCredential? Credential { get; }

	/// <summary>Gets the zero-based credential attempt count.</summary>
	public int CredentialAttempt { get; }

	/// <summary>Gets the optional credential resolver.</summary>
	public IArchiveCredentialResolver? CredentialResolver { get; }

	/// <summary>Initializes an archive mount request.</summary>
	/// <param name="source">The owning storage source.</param>
	/// <param name="archiveModel">The archive item model.</param>
	/// <param name="credential">The optional credential.</param>
	/// <param name="credentialAttempt">The credential attempt count.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	public ArchiveMountRequest(IStorageSource source, IStorableModel archiveModel, ArchiveCredential? credential = null, int credentialAttempt = 0, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(archiveModel);
		ArgumentOutOfRangeException.ThrowIfNegative(credentialAttempt);

		Source = source;
		ArchiveModel = archiveModel;
		Credential = credential;
		CredentialAttempt = credentialAttempt;
		CredentialResolver = credentialResolver;
	}
}

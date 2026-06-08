// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;

namespace Files.Core.Storage.Archives;

public sealed record ArchiveMountRequest
{
	public ArchiveMountRequest(
		IStorageSource source,
		IStorableModel archiveModel,
		ArchiveCredential? credential = null,
		int credentialAttempt = 0,
		IArchiveCredentialResolver? credentialResolver = null)
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

	public IStorageSource Source { get; }

	public IStorableModel ArchiveModel { get; }

	public StorableReference Archive => ArchiveModel.Reference;

	public ArchiveCredential? Credential { get; }

	public int CredentialAttempt { get; }

	public IArchiveCredentialResolver? CredentialResolver { get; }
}

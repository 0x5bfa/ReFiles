// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Resolves credentials without coupling Files.Core to a UI framework.
/// </summary>
public interface IArchiveCredentialResolver
{
	/// <summary>Resolves credentials for an archive challenge.</summary>
	/// <param name="challenge">The credential challenge.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The credential, or <see langword="null"/> when no credential is supplied.</returns>
	ValueTask<ArchiveCredential?> ResolveAsync(ArchiveCredentialChallenge challenge, CancellationToken cancellationToken = default);
}

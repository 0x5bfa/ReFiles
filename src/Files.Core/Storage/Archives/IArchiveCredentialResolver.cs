// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Resolves credentials without coupling Files.Core to a UI framework.
/// </summary>
public interface IArchiveCredentialResolver
{
	ValueTask<ArchiveCredential?> ResolveAsync(ArchiveCredentialChallenge challenge, CancellationToken cancellationToken = default);
}

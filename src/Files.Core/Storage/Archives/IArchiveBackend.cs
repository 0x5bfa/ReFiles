// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Mounts archives using one archive technology.</summary>
public interface IArchiveBackend
{
	/// <summary>Gets the unique backend identifier.</summary>
	string Id { get; }

	/// <summary>Gets the backend priority.</summary>
	int Priority { get; }

	/// <summary>Gets a value indicating whether encrypted archives are supported.</summary>
	bool SupportsEncryptedArchives { get; }

	/// <summary>Attempts to mount an archive.</summary>
	/// <param name="request">The mount request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The mount result.</returns>
	ValueTask<ArchiveMountResult> TryMountAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default);
}

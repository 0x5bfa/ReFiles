// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

public interface IArchiveBackend
{
	string Id { get; }

	int Priority { get; }

	bool SupportsEncryptedArchives { get; }

	ValueTask<ArchiveMountResult> TryMountAsync(
		ArchiveMountRequest request,
		CancellationToken cancellationToken = default);
}

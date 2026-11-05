// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Transfers;

/// <summary>Stages destination content without exposing a provider's native operations.</summary>
public interface IStorageImportProvider
{
	/// <summary>Determines whether this provider can import into a destination.</summary>
	/// <param name="destination">The destination folder.</param>
	/// <returns>Whether the destination is supported.</returns>
	bool CanImport(StorableReference destination);

	/// <summary>Creates a caller-owned staging transaction.</summary>
	/// <param name="destination">The destination folder.</param>
	/// <param name="name">The requested item name.</param>
	/// <param name="conflict">The conflict policy.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The unpublished destination transaction.</returns>
	ValueTask<IStorageImportFile> StageAsync(StorableReference destination, string name, StorageConflictBehavior conflict, CancellationToken cancellationToken);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Transfers;

/// <summary>Owns staged destination data and reports publication independently of cleanup.</summary>
public interface IStorageImportFile : IAsyncDisposable
{
	/// <summary>Gets the borrowed output, valid until publication or disposal.</summary>
	Stream Output { get; }
	/// <summary>Gets whether publication has begun.</summary>
	bool PublicationAttempted { get; }
	/// <summary>Gets the acknowledged committed item, if any.</summary>
	StorableReference? CommittedItem { get; }

	/// <summary>Publishes staged data under the destination provider's ownership rules.</summary>
	/// <param name="control">The optional interruption handler.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The acknowledged publication result.</returns>
	ValueTask<StorageOperationResult> CommitAsync(IStorageOperationControl? control, CancellationToken cancellationToken);
}

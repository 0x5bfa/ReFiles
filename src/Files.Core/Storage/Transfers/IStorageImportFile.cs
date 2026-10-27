// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Transfers;

internal interface IStorageImportFile : IAsyncDisposable
{
	Stream Output { get; }
	bool PublicationAttempted { get; }
	StorableReference? CommittedItem { get; }
	ValueTask<StorageOperationResult> CommitAsync(IStorageOperationControl? control, CancellationToken cancellationToken);
}

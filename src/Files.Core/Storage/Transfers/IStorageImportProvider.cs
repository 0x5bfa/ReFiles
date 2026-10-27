// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Transfers;

internal interface IStorageImportProvider
{
	bool CanImport(StorableReference destination);
	ValueTask<IStorageImportFile> StageAsync(StorableReference destination, string name, StorageConflictBehavior conflict, CancellationToken cancellationToken);
}

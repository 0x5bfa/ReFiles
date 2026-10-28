// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;
using Files.Core.Storage.Transfers;
using Files.Core.Storage.Windows;

namespace Files.Core;

public sealed partial class FilesCoreBuilder
{
	private const string StorageTransfersModule = "Files.Core.Storage.Transfers";

	/// <summary>Adds file transfers into supported Windows file-system destinations.</summary>
	/// <param name="readers">The optional archive reader selection.</param>
	/// <param name="credentials">The optional execution-owned archive credential resolver.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddStorageTransfers(ArchiveBackendSelector? readers = null, IArchiveCredentialResolver? credentials = null)
	{
		EnsureLocalStorageOperations();
		if (TryAddModule(StorageTransfersModule))
		{
			readers ??= new ArchiveBackendSelector([new SevenZipArchiveBackend()]);
			AddArchiveBrowsing(readers.Backends, credentials);
			AddStorageOperationHandlerFactory(workspace => new StorageTransferHandler(workspace, readers, credentials,
				workspace.Sources.OfType<WindowsStorageSource>().Select(source => (IStorageImportProvider)new WindowsStorageImportProvider(source)).ToArray()));
		}

		return this;
	}
}

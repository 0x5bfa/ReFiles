// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;
using Files.Core.Storage.Transfers;
using Files.Core.Windows;

namespace Files.Core.Composition;

/// <summary>Registers operation-owned cross-source file copying after native provider routes.</summary>
public static class StorageTransferFilesCoreBuilderExtensions
{
	/// <summary>Adds file transfers into supported Windows file-system destinations.</summary>
	/// <param name="builder">The runtime builder.</param>
	/// <param name="readers">The optional archive reader selection.</param>
	/// <param name="credentials">The optional execution-owned archive credential resolver.</param>
	/// <returns>The configured builder.</returns>
	public static FilesCoreBuilder AddStorageTransfers(this FilesCoreBuilder builder, ArchiveBackendSelector? readers = null, IArchiveCredentialResolver? credentials = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.EnsureLocalStorageOperations();
		if (builder.TryAddModule("Files.Core.Storage.Transfers"))
		{
			readers ??= new ArchiveBackendSelector([new SevenZipArchiveBackend()]);
			builder.AddArchiveBrowsing(readers.Backends, credentials);
			builder.AddStorageOperationHandlerFactory(workspace => new StorageTransferHandler(workspace, readers, credentials,
				workspace.Sources.OfType<WindowsStorageSource>().Select(source => (IStorageImportProvider)new WindowsStorageImportProvider(source)).ToArray()));
		}

		return builder;
	}
}

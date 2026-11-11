// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SharpCompress;
using Files.Core.Storage.Transfers;
using Files.Core.Storage.Windows;

namespace Files.Core;

public static partial class FilesProviderExtensions
{
	private const string StorageTransfersModule = "Files.Core.Storage.Transfers";

	/// <summary>Adds file transfers into supported Windows file-system destinations.</summary>
	/// <param name="readers">The optional archive reader selection.</param>
	/// <param name="credentials">The optional execution-owned archive credential resolver.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	public static TBuilder AddStorageTransfers<TBuilder>(this TBuilder builder, ArchiveBackendSelector? readers = null, IArchiveCredentialResolver? credentials = null)
		where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.EnsureLocalStorageOperations();
		if (builder.TryAddModule(StorageTransfersModule))
		{
			AddFtpContentCapabilities(builder);
			readers ??= new ArchiveBackendSelector([new SharpCompressArchiveBackend()]);
			builder.AddArchiveBrowsing(readers.Backends, credentials);
			builder.ConfigureStorageTransfers(readers, credentials,
				workspace => workspace.Sources.OfType<WindowsStorageSource>().Select(source => (IStorageImportProvider)new WindowsStorageImportProvider(source)).ToArray());
		}

		return builder;
	}
}

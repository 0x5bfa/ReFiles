// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Capabilities.Content;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;

namespace Files.Core;

public static partial class FilesProviderExtensions
{
	private const string ArchiveBrowsingModule = "Files.Core.Archives.Browsing";

	/// <summary>Registers archive capabilities and browsing with the standard reader.</summary>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	public static TBuilder AddArchiveBrowsing<TBuilder>(this TBuilder builder, IArchiveCredentialResolver? credentialResolver = null) where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddArchiveBrowsing([new SevenZipArchiveBackend()], credentialResolver);
	}

	/// <summary>Registers archive capabilities and browsing with the specified readers.</summary>
	/// <param name="backends">The archive readers.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <typeparam name="TBuilder">The runtime registration implementation.</typeparam>
	/// <param name="builder">The runtime registration target.</param>
	/// <returns>This builder.</returns>
	public static TBuilder AddArchiveBrowsing<TBuilder>(this TBuilder builder, IEnumerable<IArchiveBackend> backends, IArchiveCredentialResolver? credentialResolver = null)
		where TBuilder : class, IProviderRuntimeBuilder
	{
		ArgumentNullException.ThrowIfNull(builder);

		ArgumentNullException.ThrowIfNull(backends);

		if (!builder.TryAddModule(ArchiveBrowsingModule))
		{
			return builder;
		}

		var readers = new ArchiveBackendSelector(backends);
		builder.Capabilities.Add<IReadableContent>(new ArchiveContentSourceFactory(), lifetime: CapabilityLifetime.Shared);
		builder.Capabilities.SetCombiner<IArchiveSource>(new PriorityCapabilityCombiner<IArchiveSource>())
			.Add<IArchiveSource>(new ArchiveSourceFactory(readers: readers, credentials: credentialResolver), priority: 100, origin: "Archive models");
		builder.ConfigureArchiveBrowsing(readers, credentialResolver);

		return builder;
	}
}

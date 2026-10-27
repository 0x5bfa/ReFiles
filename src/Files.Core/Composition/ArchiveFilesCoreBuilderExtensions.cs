// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;

namespace Files.Core.Composition;

/// <summary>Registers archive models and browsing over a common provider.</summary>
public static class ArchiveFilesCoreBuilderExtensions
{
	private const string ArchiveBrowsingModule = "Files.Core.Archives.Browsing";

	/// <summary>Registers the standard archive reader and application-model capability.</summary>
	/// <param name="builder">The Core builder.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddArchiveBrowsing(this FilesCoreBuilder builder, IArchiveCredentialResolver? credentialResolver = null)
	{
		return builder.AddArchiveBrowsing([new SevenZipArchiveBackend()], credentialResolver);
	}

	/// <summary>Registers explicit readers behind the common archive provider.</summary>
	/// <param name="builder">The Core builder.</param>
	/// <param name="backends">The archive readers.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddArchiveBrowsing(this FilesCoreBuilder builder, IEnumerable<IArchiveBackend> backends, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		ArgumentNullException.ThrowIfNull(backends);

		if (!builder.TryAddModule(ArchiveBrowsingModule))
		{
			return builder;
		}

		var readers = new ArchiveBackendSelector(backends);
		builder.Capabilities.SetCombiner<IArchiveSource>(new PriorityCapabilityCombiner<IArchiveSource>())
			.Add<IArchiveSource>(new ArchiveSourceFactory(readers: readers, credentials: credentialResolver), priority: 100, origin: "Archive models");
		builder.AddStorageBrowseLocationHandler(workspace => new ArchiveBrowseLocationHandler(workspace, workspace.ModelFactory, readers, credentialResolver));

		return builder;
	}
}

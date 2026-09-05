// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;
using Files.Core.Windows;

namespace Files.Core.Composition;

/// <summary>Provides archive browsing composition extensions.</summary>
public static class ArchiveFilesCoreBuilderExtensions
{
	private const string ArchiveBrowsingModule = "Files.Core.Archives.Browsing";

	/// <summary>Adds the built-in archive browsing backends.</summary>
	/// <param name="builder">The Files.Core builder.</param>
	/// <param name="credentialResolver">The optional archive credential resolver.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddArchiveBrowsing(this FilesCoreBuilder builder, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var sevenZipBackend = new SevenZipArchiveBackend();

		return builder.AddArchiveBrowsing([new WindowsShellArchiveBackend(), sevenZipBackend,], sevenZipBackend, credentialResolver);
	}

	/// <summary>Adds archive browsing with explicit backends.</summary>
	/// <param name="builder">The Files.Core builder.</param>
	/// <param name="backends">The archive backends.</param>
	/// <param name="probe">The optional archive probe.</param>
	/// <param name="credentialResolver">The optional archive credential resolver.</param>
	/// <returns>The builder.</returns>
	public static FilesCoreBuilder AddArchiveBrowsing(
		this FilesCoreBuilder builder,
		IEnumerable<IArchiveBackend> backends,
		IArchiveProbe? probe = null,
		IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(backends);

		if (!builder.TryAddModule(ArchiveBrowsingModule))
		{
			return builder;
		}

		var selector = new ArchiveBackendSelector(backends, probe);
		builder.Capabilities.SetCombiner<IArchiveSource>(new PriorityCapabilityCombiner<IArchiveSource>()).Add<IArchiveSource>(new ArchiveSourceFactory(), priority: 100, origin: "Archive browsing");
		builder.AddStorageBrowseLocationHandler(workspace => new ArchiveBrowseLocationHandler(workspace, workspace.ModelFactory, selector, credentialResolver));

		return builder;
	}
}

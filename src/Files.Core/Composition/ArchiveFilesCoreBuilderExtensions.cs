// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Archives;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;

namespace Files.Core.Composition;

public static class ArchiveFilesCoreBuilderExtensions
{
	private const string ArchiveBrowsingModule =
		"Files.Core.Archives.Browsing";

	public static FilesCoreBuilder AddArchiveBrowsing(
		this FilesCoreBuilder builder,
		IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		var sevenZipBackend = new SevenZipArchiveBackend();
		return builder.AddArchiveBrowsing(
			[
				new WindowsShellArchiveBackend(),
				sevenZipBackend,
			],
			sevenZipBackend,
			credentialResolver);
	}

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

		var selector = new ArchiveBackendSelector(
			backends,
			probe);
		builder.ItemFeatures
			.SetCombiner<IArchiveSource>(
				new PriorityItemFeatureCombiner<IArchiveSource>())
			.Add<IArchiveSource>(
				new ArchiveSourceFactory(),
				priority: 100,
				origin: "Archive browsing");
		builder.AddBrowseLocationHandler(
			dataRoot => new ArchiveBrowseLocationHandler(
				dataRoot,
				selector,
				credentialResolver));
		return builder;
	}
}

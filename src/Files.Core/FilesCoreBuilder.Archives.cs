// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Storage.Archives;
using Files.Core.Storage.Archives.SevenZip;

namespace Files.Core;

public sealed partial class FilesCoreBuilder
{
	private const string ArchiveBrowsingModule = "Files.Core.Archives.Browsing";

	/// <summary>Registers archive capabilities and browsing with the standard reader.</summary>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddArchiveBrowsing(IArchiveCredentialResolver? credentialResolver = null)
	{
		return AddArchiveBrowsing([new SevenZipArchiveBackend()], credentialResolver);
	}

	/// <summary>Registers archive capabilities and browsing with the specified readers.</summary>
	/// <param name="backends">The archive readers.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	/// <returns>This builder.</returns>
	public FilesCoreBuilder AddArchiveBrowsing(IEnumerable<IArchiveBackend> backends, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(backends);

		if (!TryAddModule(ArchiveBrowsingModule))
		{
			return this;
		}

		var readers = new ArchiveBackendSelector(backends);
		Capabilities.SetCombiner<IArchiveSource>(new PriorityCapabilityCombiner<IArchiveSource>())
			.Add<IArchiveSource>(new ArchiveSourceFactory(readers: readers, credentials: credentialResolver), priority: 100, origin: "Archive models");
		AddStorageBrowseLocationHandler(workspace => new ArchiveBrowseLocationHandler(workspace, workspace.ModelFactory, readers, credentialResolver));

		return this;
	}
}

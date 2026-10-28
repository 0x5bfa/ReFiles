// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Browsing;

/// <summary>Adapts independently opened archive models to browse locations.</summary>
public sealed class ArchiveBrowseLocationHandler : IBrowseLocationHandler
{
	private readonly IStorageWorkspace _workspace;
	private readonly IStorableModelFactory _models;
	private readonly ArchiveBackendSelector _readers;
	private readonly IArchiveCredentialResolver? _credentials;

	/// <summary>Creates an archive navigation adapter.</summary>
	/// <param name="workspace">The borrowed configured workspace.</param>
	/// <param name="modelFactory">The item model factory.</param>
	/// <param name="backendSelector">The configured readers.</param>
	/// <param name="credentialResolver">The optional credential resolver.</param>
	public ArchiveBrowseLocationHandler(IStorageWorkspace workspace, IStorableModelFactory modelFactory, ArchiveBackendSelector backendSelector, IArchiveCredentialResolver? credentialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(modelFactory);

		ArgumentNullException.ThrowIfNull(backendSelector);

		_workspace = workspace;
		_models = modelFactory;
		_readers = backendSelector;
		_credentials = credentialResolver;
	}

	/// <inheritdoc />
	public bool CanHandle(BrowseLocation location)
	{
		return location is ArchiveLocation;
	}

	/// <inheritdoc />
	public async ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		if (location is not ArchiveLocation archiveLocation)
		{
			throw new ArgumentException("The location must identify an archive.", nameof(location));
		}

		var source = _workspace.Sources.Single(source => source.SourceId == archiveLocation.Archive.SourceId);
		var archive = await ArchiveModel.OpenAsync(source, archiveLocation.Locator, _models, _readers, _credentials, cancellationToken).ConfigureAwait(false);
		IFolderModel? folder = null;
		try
		{
			folder = await archive.OpenFolderAsync(archiveLocation.EntryPath, cancellationToken).ConfigureAwait(false);
			var parent = await GetParentAsync(archiveLocation, source, cancellationToken).ConfigureAwait(false);

			return new ArchiveBrowseLocationContext(archiveLocation, archive, folder, _workspace, parent);
		}
		catch
		{
			try
			{
				if (folder is not null)
				{
					await folder.DisposeAsync().ConfigureAwait(false);
				}
			}
			finally
			{
				await archive.DisposeAsync().ConfigureAwait(false);
			}

			throw;
		}
	}

	private async ValueTask<BrowseLocation?> GetParentAsync(ArchiveLocation location, IStorageSource source, CancellationToken cancellationToken)
	{
		if (location.EntryPath.Length != 0)
		{
			return new ArchiveLocation(location.Locator, ArchiveEntryPath.GetParent(location.EntryPath));
		}

		if (location.Locator.Parent is { } parentArchive)
		{
			return new ArchiveLocation(parentArchive, ArchiveEntryPath.GetParent(location.Locator.EntryPath!));
		}

		await using var container = await _workspace.ResolveAsync(location.Archive, cancellationToken).ConfigureAwait(false);
		if (container.GetCoreModel() is not IStorableChild child || await child.GetParentAsync(cancellationToken).ConfigureAwait(false) is not { } parent)
		{
			return null;
		}

		await using var model = _models.Create(source, parent);

		return new FolderLocation(model.Reference);
	}
}

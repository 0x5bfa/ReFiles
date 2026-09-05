// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Models;

namespace Files.Core.Windows;

internal sealed class WindowsSearchBrowseLocationHandler : IBrowseLocationHandler
{
	private readonly WindowsStorageSource _source;

	private readonly StorageWorkspace _workspace;

	internal WindowsSearchBrowseLocationHandler(StorageWorkspace workspace, WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		ArgumentNullException.ThrowIfNull(source);

		_workspace = workspace;
		_source = source;
	}

	/// <inheritdoc />
	public bool CanHandle(BrowseLocation location) => location is SearchLocation search && (search.Scope is null || search.Scope.SourceId == _source.SourceId);

	/// <inheritdoc />
	public async ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		if (location is not SearchLocation searchLocation)
		{
			throw new ArgumentException("The location must identify a search.", nameof(location));
		}

		var searchFolder = await _source.CreateSearchFolderAsync(searchLocation.Query, searchLocation.Scope, cancellationToken).ConfigureAwait(false);
		var model = _workspace.ModelFactory.Create(_source, searchFolder);
		if (model is not IFolderModel folderModel)
		{
			await model.DisposeAsync().ConfigureAwait(false);

			throw new InvalidOperationException("The Windows Shell search did not create a folder model.");
		}

		return new WindowsSearchBrowseLocationContext(searchLocation, folderModel, _workspace);
	}
}

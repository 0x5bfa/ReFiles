// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Models;

namespace Files.Core.Browsing;

public sealed class FolderBrowseLocationHandler : IBrowseLocationHandler
{
	private readonly IFilesDataRoot dataRoot;

	public FolderBrowseLocationHandler(IFilesDataRoot dataRoot)
	{
		ArgumentNullException.ThrowIfNull(dataRoot);
		this.dataRoot = dataRoot;
	}

	public bool CanHandle(BrowseLocation location) => location is FolderLocation;

	public async ValueTask<IBrowseLocationContext> OpenAsync(
		BrowseLocation location,
		CancellationToken cancellationToken = default)
	{
		if (location is not FolderLocation folderLocation)
		{
			throw new ArgumentException("The location must identify a folder.", nameof(location));
		}

		var model = await dataRoot.ResolveAsync(folderLocation.Folder, cancellationToken).ConfigureAwait(false);

		if (model is not IFolderModel folderModel)
		{
			await model.DisposeAsync().ConfigureAwait(false);
			throw new InvalidOperationException($"Item '{folderLocation.Folder.ItemId}' is not a folder.");
		}

		return new FolderBrowseLocationContext(folderLocation, folderModel, dataRoot);
	}
}

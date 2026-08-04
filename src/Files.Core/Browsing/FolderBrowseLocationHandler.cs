// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Data;
using Files.Core.Models;

namespace Files.Core.Browsing;

/// <summary>Opens browse contexts for storage folders.</summary>
public sealed class FolderBrowseLocationHandler : IBrowseLocationHandler
{
	private readonly IStorageWorkspace _workspace;

	/// <summary>Initializes a folder browse location handler.</summary>
	/// <param name="workspace">The storage workspace.</param>
	public FolderBrowseLocationHandler(IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(workspace);

		_workspace = workspace;
	}

	/// <inheritdoc />
	public bool CanHandle(BrowseLocation location) => location is FolderLocation;

	/// <inheritdoc />
	public async ValueTask<IBrowseLocationContext> OpenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		if (location is not FolderLocation folderLocation)
		{
			throw new ArgumentException("The location must identify a folder.", nameof(location));
		}

		var model = await _workspace.ResolveAsync(folderLocation.Folder, cancellationToken).ConfigureAwait(false);

		if (model is not IFolderModel folderModel)
		{
			await model.DisposeAsync().ConfigureAwait(false);
			throw new InvalidOperationException($"Item '{folderLocation.Folder.ItemId}' is not a folder.");
		}

		return new FolderBrowseLocationContext(folderLocation, folderModel, _workspace);
	}
}

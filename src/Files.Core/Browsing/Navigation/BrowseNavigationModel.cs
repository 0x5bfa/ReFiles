// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Capabilities.Thumbnails;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Files.Core.Browsing;

/// <summary>Provides scoped hierarchy and child navigation independently of a view.</summary>
public sealed class BrowseNavigationModel
{
	private const int NavigationThumbnailSize = 16;
	private readonly IBrowseSession _session;
	private readonly IStorageWorkspace _workspace;
	private readonly ILogger<BrowseNavigationModel> _logger;

	/// <summary>Initializes navigation over borrowed application state.</summary>
	/// <param name="session">The borrowed browse state.</param>
	/// <param name="workspace">The borrowed storage workspace.</param>
	/// <param name="logger">The optional logger.</param>
	public BrowseNavigationModel(IBrowseSession session, IStorageWorkspace workspace, ILogger<BrowseNavigationModel>? logger = null)
	{
		ArgumentNullException.ThrowIfNull(session);

		ArgumentNullException.ThrowIfNull(workspace);

		_session = session;
		_workspace = workspace;
		_logger = logger ?? NullLogger<BrowseNavigationModel>.Instance;
	}

	/// <summary>Gets the hierarchy of the current location.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>Navigation values that do not retain provider resources.</returns>
	public Task<IReadOnlyList<BrowseNavigationEntry>> GetBreadcrumbsAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return _session.Location switch
		{
			FolderLocation folder => CreateFolderBreadcrumbItemsAsync(folder, cancellationToken),
			ArchiveLocation archive => CreateArchiveBreadcrumbItemsAsync(archive, cancellationToken),
			{ } location => Task.FromResult<IReadOnlyList<BrowseNavigationEntry>>([new BrowseNavigationEntry(string.Empty, location, location is HomeLocation)]),
			null => Task.FromResult<IReadOnlyList<BrowseNavigationEntry>>([]),
		};
	}

	/// <summary>Gets child locations for a navigation target.</summary>
	/// <param name="location">The parent location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The child locations and encoded icons.</returns>
	public Task<IReadOnlyList<BrowseNavigationEntry>> GetChildrenAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		cancellationToken.ThrowIfCancellationRequested();

		return location switch
		{
			HomeLocation => CreateHomeBreadcrumbChildrenAsync(cancellationToken),
			FolderLocation folder => CreateFolderBreadcrumbChildrenAsync(folder, cancellationToken),
			ArchiveLocation archive => CreateArchiveBreadcrumbChildrenAsync(archive, cancellationToken),
			_ => Task.FromResult<IReadOnlyList<BrowseNavigationEntry>>([]),
		};
	}

	private async Task<IReadOnlyList<BrowseNavigationEntry>> CreateFolderBreadcrumbItemsAsync(FolderLocation location, CancellationToken cancellationToken)
	{
		if (_session.Context is not { Location: FolderLocation contextLocation, LocationModel: IFolderModel currentFolder } || !Equals(contextLocation, location))
		{
			return (BrowseNavigationEntry[])[new BrowseNavigationEntry((_session.Context?.LocationModel?.Name ?? string.Empty), location, false)];
		}

		var reversedItems = new List<(string Text, FolderLocation Location)>();
		var visitedLocations = new HashSet<StorableReference>();
		IFolderModel? model = currentFolder;
		IFolderModel? ownedModel = null;
		try
		{
			while (model is not null)
			{
				if (!visitedLocations.Add(model.Reference))
				{
					break;
				}

				reversedItems.Add((model.Name, new FolderLocation(model.Reference)));
				var parent = await model.GetParentAsync(cancellationToken).ConfigureAwait(false);
				if (ownedModel is not null)
				{
					await ownedModel.DisposeAsync().ConfigureAwait(false);
				}

				ownedModel = parent;
				model = parent;
			}
		}
		finally
		{
			if (ownedModel is not null)
			{
				await ownedModel.DisposeAsync().ConfigureAwait(false);
			}
		}

		reversedItems.Reverse();

		var leafHasChildren = await HasFolderChildrenAsync(currentFolder, cancellationToken).ConfigureAwait(false);
		var items = new List<BrowseNavigationEntry>(reversedItems.Count);
		for (var index = 0; index < reversedItems.Count; index++)
		{
			var item = reversedItems[index];
			items.Add(new BrowseNavigationEntry(item.Text, item.Location, index < reversedItems.Count - 1 || leafHasChildren));
		}

		return items;
	}

	private async Task<IReadOnlyList<BrowseNavigationEntry>> CreateArchiveBreadcrumbItemsAsync(ArchiveLocation location, CancellationToken cancellationToken)
	{
		await using var archiveModel = await _workspace.ResolveAsync(location.Archive, cancellationToken).ConfigureAwait(false);
		var archiveName = location.Locator.EntryPath is { } nestedPath ? ArchiveEntryPath.GetName(nestedPath) : archiveModel.Name;
		var context = _session.Context;
		var leafHasChildren = context is { Location: ArchiveLocation contextLocation, LocationModel: IFolderModel currentFolder }
			&& Equals(contextLocation, location)
			&& await HasFolderChildrenAsync(currentFolder, cancellationToken).ConfigureAwait(false);
		var segments = string.IsNullOrEmpty(location.EntryPath) ? [] : location.EntryPath.Split('/');
		var items = new List<BrowseNavigationEntry>(segments.Length + 1)
		{
			new(archiveName, new ArchiveLocation(location.Locator), segments.Length > 0 || leafHasChildren),
		};
		var entryPath = string.Empty;
		for (var index = 0; index < segments.Length; index++)
		{
			entryPath = ArchiveEntryPath.Combine(entryPath, segments[index]);
			items.Add(new BrowseNavigationEntry(segments[index], new ArchiveLocation(location.Locator, entryPath), index < segments.Length - 1 || leafHasChildren));
		}

		return items;
	}

	private async Task<IReadOnlyList<BrowseNavigationEntry>> CreateHomeBreadcrumbChildrenAsync(CancellationToken cancellationToken)
	{
		var items = new List<BrowseNavigationEntry>();
		foreach (var source in _workspace.Sources)
		{
			await foreach (var root in _workspace.GetRootsAsync(source.SourceId, cancellationToken).ConfigureAwait(false))
			{
				try
				{
					if (!_session.DisplaySettings.ShowHiddenItems && root.IsHidden)
					{
						continue;
					}

					var thumbnail = await LoadBreadcrumbThumbnailAsync(root, cancellationToken, _logger).ConfigureAwait(false);
					items.Add(new BrowseNavigationEntry(root.Name, new FolderLocation(root.Reference), false, thumbnail));
				}
				finally
				{
					await root.DisposeAsync().ConfigureAwait(false);
				}
			}
		}

		return items;
	}

	private async Task<IReadOnlyList<BrowseNavigationEntry>> CreateFolderBreadcrumbChildrenAsync(FolderLocation location, CancellationToken cancellationToken)
	{
		var items = new List<BrowseNavigationEntry>();
		await using var model = await _workspace.ResolveAsync(location.Folder, cancellationToken).ConfigureAwait(false);
		if (model is not IFolderModel folder)
		{
			return items;
		}

		await foreach (var child in folder.GetItemsAsync(StorableType.Folder, cancellationToken).ConfigureAwait(false))
		{
			try
			{
				if (!_session.DisplaySettings.ShowHiddenItems && child.IsHidden)
				{
					continue;
				}

				var thumbnail = await LoadBreadcrumbThumbnailAsync(child, cancellationToken, _logger).ConfigureAwait(false);
				items.Add(new BrowseNavigationEntry(child.Name, new FolderLocation(child.Reference), false, thumbnail));
			}
			finally
			{
				await child.DisposeAsync().ConfigureAwait(false);
			}
		}

		return items;
	}

	private async Task<IReadOnlyList<BrowseNavigationEntry>> CreateArchiveBreadcrumbChildrenAsync(ArchiveLocation location, CancellationToken cancellationToken)
	{
		if (_session.Context is not ArchiveBrowseLocationContext context || context.Location is not ArchiveLocation contextLocation || contextLocation.Locator != location.Locator)
		{
			return [];
		}

		var items = new List<BrowseNavigationEntry>();
		await foreach (var child in context.GetItemsAsync(location, StorableType.Folder, cancellationToken).ConfigureAwait(false))
		{
			try
			{
				if (!_session.DisplaySettings.ShowHiddenItems && child.IsHidden)
				{
					continue;
				}

				var entryPath = ArchiveEntryPath.Combine(location.EntryPath, child.Name);
				var thumbnail = await LoadBreadcrumbThumbnailAsync(child, cancellationToken, _logger).ConfigureAwait(false);
				items.Add(new BrowseNavigationEntry(child.Name, new ArchiveLocation(location.Locator, entryPath), false, thumbnail));
			}
			finally
			{
				await child.DisposeAsync().ConfigureAwait(false);
			}
		}

		return items;
	}

	private static async Task<ThumbnailResult?> LoadBreadcrumbThumbnailAsync(IStorableModel model, CancellationToken cancellationToken, ILogger<BrowseNavigationModel> logger)
	{
		if (model.Get<IThumbnailSource>() is not { } source)
		{
			return null;
		}

		try
		{
			return await source.GetThumbnailAsync(new ThumbnailRequest(NavigationThumbnailSize, ThumbnailMode.Icon), cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			if (logger.IsEnabled(LogLevel.Debug))
			{
				logger.LogDebug("{Component}: {Message}", "NavigationToolbar", $"Breadcrumb thumbnail failed for '{model.Reference.ItemId}': {error.GetType().Name}");
			}

			return null;
		}
	}

	private async Task<bool> HasFolderChildrenAsync(IFolderModel folder, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var child in folder.GetItemsAsync(StorableType.Folder, cancellationToken).ConfigureAwait(false))
			{
				try
				{
					if (!_session.DisplaySettings.ShowHiddenItems && child.IsHidden)
					{
						continue;
					}

					return true;
				}
				finally
				{
					await child.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
		catch (Exception) when (!cancellationToken.IsCancellationRequested)
		{
		}

		return false;
	}

}

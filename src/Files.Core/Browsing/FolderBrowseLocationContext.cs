// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Windows;
using OwlCore.Storage;

namespace Files.Core.Browsing;

/// <summary>
/// Keeps a resolved folder model alive for the duration of a browse location.
/// </summary>
public sealed class FolderBrowseLocationContext : IBrowseLocationContext, IBrowseLocationItemResolver, IBrowseLocationItemSorter, IInteractiveBrowseLocationContext
{
	private readonly FolderLocation _location;

	private readonly IFolderModel _folderModel;

	private readonly IStorageWorkspace _workspace;

	private int _isDisposed;

	/// <inheritdoc />
	public BrowseLocation Location => _location;

	/// <inheritdoc />
	public IStorableModel LocationModel => _folderModel;

	/// <summary>Initializes a folder browse context and takes ownership of the folder model.</summary>
	/// <param name="location">The folder location.</param>
	/// <param name="folderModel">The folder model.</param>
	/// <param name="workspace">The storage workspace.</param>
	public FolderBrowseLocationContext(FolderLocation location, IFolderModel folderModel, IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(folderModel);
		ArgumentNullException.ThrowIfNull(workspace);

		_location = location;
		_folderModel = folderModel;
		_workspace = workspace;
	}

	/// <summary>
	/// Gets the Windows Shell columns exposed by this folder when the folder is backed by Windows Shell.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The Shell column metadata, or <see langword="null"/> for non-Windows storage.</returns>
	public async ValueTask<WindowsShellColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		if (_folderModel.GetCoreModel() is not WindowsFolder folder)
		{
			return null;
		}

		return await folder.GetColumnsAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<IStorableModel> GetItemsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		await foreach (var item in _folderModel.GetItemsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <inheritdoc />
	public ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
		ArgumentNullException.ThrowIfNull(reference);

		return _workspace.ResolveAsync(reference, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			await _folderModel.DisposeAsync().ConfigureAwait(false);
		}

		GC.SuppressFinalize(this);
	}

	async IAsyncEnumerable<IStorableModel> IInteractiveBrowseLocationContext.GetItemsAsync(nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		var items = ownerWindowHandle is not 0 && _folderModel is FolderModel folderModel
			? folderModel.GetItemsAsync(StorableType.All, ownerWindowHandle, cancellationToken)
			: _folderModel.GetItemsAsync(cancellationToken: cancellationToken);
		await foreach (var item in items.ConfigureAwait(false))
		{
			yield return item;
		}
	}

	async ValueTask<IReadOnlyList<IStorableModel>?> IBrowseLocationItemSorter.SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		if (_folderModel.GetCoreModel() is not WindowsFolder folder)
		{
			return null;
		}

		var windowsItems = new WindowsStorable[items.Count];
		var modelsByCoreModel = new Dictionary<WindowsStorable, IStorableModel>(ReferenceEqualityComparer.Instance);
		for (var index = 0; index < items.Count; index++)
		{
			if (items[index].GetCoreModel() is not WindowsStorable windowsItem)
			{
				return null;
			}

			windowsItems[index] = windowsItem;
			modelsByCoreModel.Add(windowsItem, items[index]);
		}

		var sortedItems = await folder.SortChildrenAsync(windowsItems, settings.SortPropertyId, settings.SortDirection, cancellationToken).ConfigureAwait(false);
		if (sortedItems is null)
		{
			return null;
		}

		return Array.AsReadOnly(sortedItems.Select(item => modelsByCoreModel[item]).ToArray());
	}
}

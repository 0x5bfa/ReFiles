// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.CompilerServices;
using Files.Core.Browsing;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using OwlCore.Storage;

namespace Files.Core.Windows;

internal sealed class WindowsSearchBrowseLocationContext :
	IBrowseLocationContext,
	IBrowseLocationItemResolver,
	IBrowseLocationItemSorter,
	IBrowseLocationParentResolver,
	IInteractiveBrowseLocationContext,
	IWindowsShellColumnProvider
{
	private readonly IFolderModel _folderModel;

	private readonly SearchLocation _location;

	private readonly IStorageWorkspace _workspace;

	private int _isDisposed;

	/// <inheritdoc />
	public bool CanGetParent => true;

	/// <inheritdoc />
	public BrowseLocation Location => _location;

	/// <inheritdoc />
	public IStorableModel? LocationModel => null;

	internal WindowsSearchBrowseLocationContext(SearchLocation location, IFolderModel folderModel, IStorageWorkspace workspace)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(folderModel);

		ArgumentNullException.ThrowIfNull(workspace);

		_location = location;
		_folderModel = folderModel;
		_workspace = workspace;
	}

	/// <inheritdoc />
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
	public ValueTask<BrowseLocation?> GetParentLocationAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		cancellationToken.ThrowIfCancellationRequested();

		return ValueTask.FromResult<BrowseLocation?>(_location.Scope is { } scope ? new FolderLocation(scope) : HomeLocation.Instance);
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

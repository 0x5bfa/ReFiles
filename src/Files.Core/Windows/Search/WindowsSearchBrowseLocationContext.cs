// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.CompilerServices;
using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Data;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;

namespace Files.Core.Windows;

internal sealed class WindowsSearchBrowseLocationContext :
	IBrowseLocationContext,
	IBrowseLocationItemResolver,
	IBrowseLocationItemSorter,
	IBrowseLocationParentResolver,
	IInteractiveBrowseLocationContext,
	IBrowseColumnProvider,
	IViewSettingsCapabilityProvider
{
	private readonly IFolderModel _folderModel;

	private readonly SearchLocation _location;

	private readonly IStorageWorkspace _workspace;

	private readonly Lock _columnsLock = new();
	private readonly CancellationTokenSource _lifetime = new();

	private int _isDisposed;

	private Task<BrowseColumnSet?>? _columnsTask;

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
	public async ValueTask<BrowseColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		Task<BrowseColumnSet?> columnsTask;
		lock (_columnsLock)
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

			columnsTask = _columnsTask ??= LoadColumnsAsync();
		}

		return await columnsTask.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async ValueTask<BrowseViewCapabilities?> GetViewSettingsCapabilitiesAsync(CancellationToken cancellationToken = default)
	{
		var columnSet = await GetColumnsAsync(cancellationToken).ConfigureAwait(false);

		return columnSet is null ? null : BrowseViewCapabilities.FromColumns(columnSet);
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

		return ValueTask.FromResult<BrowseLocation?>(_location.Origin);
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
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
		{
			return;
		}

		try
		{
			_lifetime.Cancel();
			Task? columns;
			lock (_columnsLock)
			{
				columns = _columnsTask;
			}

			if (columns is not null)
			{
				try
				{
					await columns.ConfigureAwait(false);
				}
				catch (Exception)
				{
					// Retrieval failures are observed by the column request; cleanup still waits for completion.
				}
			}
		}
		finally
		{
			try
			{
				await _folderModel.DisposeAsync().ConfigureAwait(false);
			}
			finally
			{
				_lifetime.Dispose();
				GC.SuppressFinalize(this);
			}
		}
	}

	private Task<BrowseColumnSet?> LoadColumnsAsync()
	{
		return _folderModel.Get<IFolderBrowseMetadata>() is { } metadata ? metadata.GetColumnsAsync(_lifetime.Token).AsTask() : Task.FromResult<BrowseColumnSet?>(null);
	}

	async IAsyncEnumerable<IStorableModel> IInteractiveBrowseLocationContext.GetItemsAsync(nint ownerWindowHandle, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		var items = ownerWindowHandle is not 0 && _folderModel is IInteractiveFolderModel folderModel
			? folderModel.GetItemsAsync(StorableType.All, ownerWindowHandle, cancellationToken)
			: _folderModel.GetItemsAsync(cancellationToken: cancellationToken);
		await foreach (var item in items.ConfigureAwait(false))
		{
			yield return item;
		}
	}

	ValueTask<IReadOnlyList<IStorableModel>?> IBrowseLocationItemSorter.SortItemsAsync(IReadOnlyList<IStorableModel> items, BrowseViewSettings settings, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

		return _folderModel.Get<IFolderBrowseMetadata>() is { } metadata
			? metadata.SortItemsAsync(items, settings, cancellationToken)
			: ValueTask.FromResult<IReadOnlyList<IStorableModel>?>(null);
	}
}

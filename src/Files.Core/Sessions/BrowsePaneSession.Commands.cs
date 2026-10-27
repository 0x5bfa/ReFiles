// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Archives;
using Files.Core.Storage.Archives;

namespace Files.Core.Sessions;

public sealed partial class BrowsePaneSession
{
	/// <summary>Gets whether the current location supports searching.</summary>
	public bool CanSearch => Location is { } location && _services?.Search?.CanSearch(location) is true;

	/// <summary>Gets whether the current selection can be deleted.</summary>
	public bool CanDeleteSelection => !BrowseSession.IsLoading && _services is not null && BrowseSession.Selection.SelectedKeys.Count > 0
		&& BrowseSession.Selection.SelectedKeys.All(key => BrowseSession.TryGet(key, out var item) && _services.Operations.CanHandle(CreateDeleteRequest(item.Reference)));

	/// <summary>Navigates to a folder resolved from an address.</summary>
	/// <param name="address">The address to resolve.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <param name="ownerWindowHandle">The optional native interaction owner.</param>
	/// <returns>The navigation operation.</returns>
	public async ValueTask NavigateToAddressAsync(StorageAddress address, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(address);

		var services = _services ?? throw new InvalidOperationException("No browse application services were configured.");
		await using var item = await services.Workspace.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
		var location = GetItemLocation(item);
		if (location is null)
		{
			throw new StorageItemNotFolderException(address);
		}

		await NavigateAsync(location, cancellationToken: cancellationToken, ownerWindowHandle: ownerWindowHandle).ConfigureAwait(false);
	}

	/// <summary>Resolves an item's folder or archive navigation target without changing pane state.</summary>
	/// <param name="reference">The captured item reference.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The target location, or <see langword="null"/> for an ordinary file.</returns>
	public async ValueTask<BrowseLocation?> GetItemLocationAsync(StorableReference reference, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(reference);

		cancellationToken.ThrowIfCancellationRequested();

		if (BrowseSession.TryGet(reference.GetKey(), out var current))
		{
			return GetItemLocation(current);
		}

		var services = _services ?? throw new InvalidOperationException("No browse application services were configured.");
		await using var item = BrowseSession.Context is IBrowseLocationItemResolver resolver
			? await resolver.ResolveAsync(reference, cancellationToken).ConfigureAwait(false)
			: await services.Workspace.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

		return GetItemLocation(item);
	}

	/// <summary>Runs a search or returns to its original location when the query is cleared.</summary>
	/// <param name="query">The query, or an empty value to exit search.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <param name="ownerWindowHandle">The optional native interaction owner.</param>
	/// <returns>The search navigation operation.</returns>
	public ValueTask SearchAsync(string query, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(query);

		var location = Location ?? HomeLocation.Instance;
		if (string.IsNullOrWhiteSpace(query))
		{
			var origin = location is SearchLocation search ? search.Origin : location;

			return NavigateAsync(origin, PaneNavigationMode.ExitSearch, cancellationToken, ownerWindowHandle);
		}

		var provider = _services?.Search ?? throw new NotSupportedException("No search provider was configured.");
		var target = provider.CreateSearch(query, location);

		return NavigateAsync(target, PaneNavigationMode.UpdateSearch, cancellationToken, ownerWindowHandle);
	}

	/// <summary>Deletes a captured selection through the configured operation boundary.</summary>
	/// <param name="cancellationToken">Cancels before acceptance; accepted job lifetime is owned by the execution service.</param>
	/// <returns>The aggregate operation result.</returns>
	public async ValueTask<StorageOperationResult> DeleteSelectionAsync(CancellationToken cancellationToken = default)
	{
		EnsureActive();

		var references = BrowseSession.Selection.SelectedKeys.Select(key => BrowseSession.TryGet(key, out var item) ? item.Reference : null).OfType<StorableReference>().ToArray();

		return await DeleteItemsAsync(references, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Deletes immutable item references captured by the caller.</summary>
	/// <param name="references">The captured operation targets.</param>
	/// <param name="cancellationToken">Cancels before acceptance.</param>
	/// <returns>The aggregate operation result.</returns>
	public async ValueTask<StorageOperationResult> DeleteItemsAsync(IReadOnlyList<StorableReference> references, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(references);

		var services = _services ?? throw new InvalidOperationException("No browse application services were configured.");
		var requests = references.Select(reference => (StorageOperationRequest)CreateDeleteRequest(reference)).ToArray();

		return await services.Operations.ExecuteAndWaitAsync(requests, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Resolves captured addresses and submits one complete transfer through the configured executor.</summary>
	/// <param name="addresses">The captured source addresses.</param>
	/// <param name="destination">The captured destination folder.</param>
	/// <param name="move">Whether to move instead of copy.</param>
	/// <param name="cancellationToken">Cancels resolution or observation, according to the execution boundary.</param>
	/// <returns>The aggregate operation result.</returns>
	public async ValueTask<StorageOperationResult> TransferAddressesAsync(IReadOnlyList<StorageAddress> addresses, StorableReference destination, bool move, CancellationToken cancellationToken = default)
	{
		EnsureActive();

		ArgumentNullException.ThrowIfNull(addresses);

		ArgumentNullException.ThrowIfNull(destination);

		var services = _services ?? throw new InvalidOperationException("No browse application services were configured.");
		var snapshot = addresses.ToArray();
		var requests = new List<StorageOperationRequest>(snapshot.Length);
		foreach (var address in snapshot)
		{
			await using var item = await services.Workspace.ResolveAsync(address, cancellationToken).ConfigureAwait(false);
			requests.Add(move ? new MoveOperationRequest(item.Reference, destination, conflictBehavior: StorageConflictBehavior.Prompt)
				: new CopyOperationRequest(item.Reference, destination, conflictBehavior: StorageConflictBehavior.Prompt));
		}

		return await services.Operations.ExecuteAndWaitAsync(requests, cancellationToken).ConfigureAwait(false);
	}

	private static BrowseLocation? GetItemLocation(IStorableModel item)
	{
		if (item.Get<IArchiveSource>() is { } archive)
		{
			return new ArchiveLocation(archive.Locator);
		}

		if (item is IFolderModel)
		{
			return item.GetCoreModel() is IArchiveEntry entry ? new ArchiveLocation(entry) : new FolderLocation(item.Reference);
		}

		return null;
	}

	private DeleteOperationRequest CreateDeleteRequest(StorableReference reference)
	{
		var canRecycle = _services!.Workspace.Sources.OfType<IStorageDeletionPolicy>().Any(policy => policy.CanRecycle(reference));

		return new DeleteOperationRequest(reference, permanently: !canRecycle);
	}
}

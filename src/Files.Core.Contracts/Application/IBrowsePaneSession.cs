// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;
using Files.Core.Capabilities;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to BrowsePaneSession state and operations.</summary>
public interface IBrowsePaneSession : IPaneContentSession, ISplittablePaneContent, IRestorablePaneContent
{
	/// <summary>Gets whether the current location supports searching.</summary>
	bool CanSearch { get; }

	/// <summary>Gets whether the current selection can be deleted.</summary>
	bool CanDeleteSelection { get; }

	/// <summary>Gets the browse state owned by this content session.</summary>
	IBrowseSession BrowseSession { get; }

	/// <summary>Gets the preview state owned by this content session.</summary>
	IBrowsePreviewModel Preview { get; }

	/// <summary>Gets the scoped navigation hierarchy, when application services are configured.</summary>
	IBrowseNavigationModel? Navigation { get; }

	/// <summary>Gets the navigation history.</summary>
	IBrowseNavigationHistory History { get; }

	/// <summary>Gets the current browse location.</summary>
	BrowseLocation? Location { get; }

	/// <summary>Gets a value indicating whether backward navigation is available.</summary>
	bool CanGoBack { get; }

	/// <summary>Gets a value indicating whether forward navigation is available.</summary>
	bool CanGoForward { get; }

	/// <summary>Gets a value indicating whether parent navigation is available.</summary>
	bool CanGoUp { get; }

	/// <summary>Occurs when navigation, history, or preview state changes.</summary>
	event EventHandler? NavigationStateChanged;

	/// <summary>Navigates to a folder resolved from an address.</summary>
	/// <param name="address">The address to resolve.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <param name="ownerWindowHandle">The optional native interaction owner.</param>
	/// <returns>The navigation operation.</returns>
	ValueTask NavigateToAddressAsync(StorageAddress address, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Resolves an item's folder or archive navigation target without changing pane state.</summary>
	/// <param name="reference">The captured item reference.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The target location, or <see langword="null"/> for an ordinary file.</returns>
	ValueTask<BrowseLocation?> GetItemLocationAsync(StorableReference reference, CancellationToken cancellationToken = default);

	/// <summary>Runs a search or returns to its original location when the query is cleared.</summary>
	/// <param name="query">The query, or an empty value to exit search.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <param name="ownerWindowHandle">The optional native interaction owner.</param>
	/// <returns>The search navigation operation.</returns>
	ValueTask SearchAsync(string query, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Deletes a captured selection through the configured operation boundary.</summary>
	/// <param name="cancellationToken">Cancels before acceptance; accepted job lifetime is owned by the execution service.</param>
	/// <returns>The aggregate operation result.</returns>
	ValueTask<StorageOperationResult> DeleteSelectionAsync(CancellationToken cancellationToken = default);

	/// <summary>Deletes immutable item references captured by the caller.</summary>
	/// <param name="references">The captured operation targets.</param>
	/// <param name="cancellationToken">Cancels before acceptance.</param>
	/// <returns>The aggregate operation result.</returns>
	ValueTask<StorageOperationResult> DeleteItemsAsync(IReadOnlyList<StorableReference> references, CancellationToken cancellationToken = default);

	/// <summary>Resolves captured addresses and submits one complete transfer through the configured executor.</summary>
	/// <param name="addresses">The captured source addresses.</param>
	/// <param name="destination">The captured destination folder.</param>
	/// <param name="move">Whether to move instead of copy.</param>
	/// <param name="cancellationToken">Cancels resolution or observation, according to the execution boundary.</param>
	/// <returns>The aggregate operation result.</returns>
	ValueTask<StorageOperationResult> TransferAddressesAsync(IReadOnlyList<StorageAddress> addresses, StorableReference destination, bool move, CancellationToken cancellationToken = default);

	/// <summary>Captures the current selection and submits an independently owned transfer job.</summary>
	/// <param name="destination">The configured destination folder.</param>
	/// <param name="move">Whether to request a supported Move instead of Copy.</param>
	/// <param name="cancellationToken">Cancels acceptance or observation, not an already accepted job.</param>
	/// <returns>The transfer result, including acknowledged effects.</returns>
	ValueTask<StorageOperationResult> TransferSelectionAsync(StorableReference destination, bool move = false, CancellationToken cancellationToken = default);

	/// <summary>Submits captured source locators without retaining the selected item models.</summary>
	/// <param name="sources">The captured source values.</param>
	/// <param name="destination">The configured destination folder.</param>
	/// <param name="move">Whether to request a supported Move instead of Copy.</param>
	/// <param name="cancellationToken">Cancels acceptance or observation according to the job boundary.</param>
	/// <returns>The transfer result, including acknowledged effects.</returns>
	ValueTask<StorageOperationResult> TransferItemsAsync(IReadOnlyList<StorageItemLocator> sources, StorableReference destination, bool move = false, CancellationToken cancellationToken = default);

	/// <summary>Navigates to a location and commits it to history.</summary>
	/// <param name="location">The target location.</param>
	/// <param name="mode">The history update mode.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns>A task that represents the navigation.</returns>
	ValueTask NavigateAsync(BrowseLocation location, PaneNavigationMode mode = PaneNavigationMode.Push, CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Navigates to the previous history entry.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when navigation completed.</returns>
	ValueTask<bool> GoBackAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Navigates to the following history entry.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when navigation completed.</returns>
	ValueTask<bool> GoForwardAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Navigates to the current location's parent.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns><see langword="true"/> when a parent was available and navigation completed.</returns>
	ValueTask<bool> GoUpAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);

	/// <summary>Restores navigation history state and its current location.</summary>
	/// <param name="restoredHistory">The state to restore.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the restore operation.</returns>
	ValueTask RestoreAsync(BrowseNavigationHistoryState restoredHistory, CancellationToken cancellationToken = default);

	/// <summary>Refreshes the current location.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="ownerWindowHandle">The native owner for UI shown while enumerating, or zero to suppress such UI.</param>
	/// <returns>A task that represents the refresh.</returns>
	ValueTask RefreshAsync(CancellationToken cancellationToken = default, nint ownerWindowHandle = 0);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.ViewSettings;

namespace Files.Core.Browsing;

/// <summary>
/// UI-agnostic state for one browser pane.
/// </summary>
public interface IBrowseSession : IDisposable, IAsyncDisposable
{
	/// <summary>Gets the current browse location.</summary>
	BrowseLocation? Location { get; }

	/// <summary>Gets the context opened for the current location.</summary>
	IBrowseLocationContext? Context { get; }

	/// <summary>Gets the generation of the current content snapshot.</summary>
	long Generation { get; }

	/// <summary>Gets the projected items in display order.</summary>
	IReadOnlyList<IStorableModel> Items { get; }

	/// <summary>Gets the version of the projected item collection.</summary>
	long ItemsVersion { get; }

	/// <summary>Gets the current selection state.</summary>
	BrowseSelectionState Selection { get; }

	/// <summary>Gets the current view settings.</summary>
	BrowseViewSettings ViewSettings { get; }

	/// <summary>Gets a value indicating whether navigation or refresh is in progress.</summary>
	bool IsLoading { get; }

	/// <summary>Gets the most recent browse error.</summary>
	Exception? Error { get; }

	/// <summary>Occurs when navigation-related state changes.</summary>
	event EventHandler? StateChanged;

	/// <summary>Occurs when the projected item collection changes.</summary>
	event EventHandler<BrowseItemsChangedEventArgs>? ItemsChanged;

	/// <summary>Occurs when prefetched presentation data changes.</summary>
	event EventHandler<BrowseItemPresentationChangedEventArgs>? ItemPresentationChanged;

	/// <summary>Occurs when the selection changes.</summary>
	event EventHandler? SelectionChanged;

	/// <summary>Navigates to a location.</summary>
	/// <param name="location">The target location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the navigation.</returns>
	ValueTask NavigateAsync(BrowseLocation location, CancellationToken cancellationToken = default);

	/// <summary>Refreshes the current location.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the refresh.</returns>
	ValueTask RefreshAsync(CancellationToken cancellationToken = default);

	/// <summary>Updates sorting and layout settings.</summary>
	/// <param name="settings">The new settings.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that represents the update.</returns>
	ValueTask UpdateViewSettingsAsync(BrowseViewSettings settings, CancellationToken cancellationToken = default);

	/// <summary>Gets prefetched presentation data for an item.</summary>
	/// <param name="key">The item key.</param>
	/// <param name="presentation">The presentation data when available.</param>
	/// <returns><see langword="true"/> when presentation data exists.</returns>
	bool TryGetPresentation(StorableKey key, out BrowseItemPresentation presentation);

	/// <summary>Replaces the selection using item keys.</summary>
	/// <param name="selectedKeys">The selected item keys.</param>
	/// <param name="focusedKey">The focused item key.</param>
	/// <param name="anchorKey">The selection anchor key.</param>
	void SetSelection(IEnumerable<StorableKey> selectedKeys, StorableKey? focusedKey, StorableKey? anchorKey);
}

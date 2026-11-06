// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to WindowSession state and operations.</summary>
public interface IWindowSession : IAsyncDisposable
{
	/// <summary>Gets the stable window identifier.</summary>
	Guid Id { get; }

	/// <summary>Gets the owned tab sessions.</summary>
	IReadOnlyList<ITabSession> Tabs { get; }

	/// <summary>Gets the active tab session.</summary>
	ITabSession? ActiveTab { get; }

	/// <summary>Gets whether a closed tab is available for restoration.</summary>
	bool CanReopenTab { get; }

	/// <summary>Occurs when the tab collection or ordering changes.</summary>
	event EventHandler? TabsChanged;

	/// <summary>Occurs when the active tab changes.</summary>
	event EventHandler? ActiveTabChanged;

	/// <summary>Opens and activates a tab.</summary>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created tab session.</returns>
	ValueTask<ITabSession> OpenTabAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default);

	/// <summary>Opens and activates a settings tab.</summary>
	/// <param name="cancellationToken">Cancels opening the tab.</param>
	/// <returns>The created settings tab.</returns>
	ValueTask<ITabSession> OpenSettingsTabAsync(CancellationToken cancellationToken = default);

	/// <summary>Opens and activates a tab containing custom pane content.</summary>
	/// <param name="contentFactory">The factory used to create the owned pane content.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created tab session.</returns>
	ValueTask<ITabSession> OpenContentTabAsync(Func<IPaneContentSession> contentFactory, CancellationToken cancellationToken = default);

	/// <summary>Opens an independently owned tab from captured state.</summary>
	/// <param name="state">The complete tab state.</param>
	/// <param name="cancellationToken">The restoration cancellation token.</param>
	/// <returns>The restored tab, published only after every pane initializes.</returns>
	ValueTask<ITabSession> OpenRestoredTabAsync(TabRestoreState state, CancellationToken cancellationToken = default);

	/// <summary>Duplicates every pane and the layout of an existing tab.</summary>
	/// <param name="tabId">The tab identity.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The duplicate, or null if the tab is absent or cannot be restored.</returns>
	ValueTask<ITabSession?> DuplicateTabAsync(Guid tabId, CancellationToken cancellationToken = default);

	/// <summary>Restores the most recently closed tab, retaining its state if restoration fails.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The restored tab, or null when no state is available.</returns>
	ValueTask<ITabSession?> ReopenTabAsync(CancellationToken cancellationToken = default);

	/// <summary>Closes and disposes a tab.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the tab existed.</returns>
	ValueTask<bool> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default);

	/// <summary>Activates a tab.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <returns><see langword="true"/> when the tab exists.</returns>
	bool SetActiveTab(Guid tabId);

	/// <summary>Moves a tab within the window.</summary>
	/// <param name="tabId">The tab identifier.</param>
	/// <param name="targetIndex">The destination index.</param>
	/// <returns><see langword="true"/> when the tab exists.</returns>
	bool MoveTab(Guid tabId, int targetIndex);
}

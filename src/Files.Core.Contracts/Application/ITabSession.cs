// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to TabSession state and operations.</summary>
public interface ITabSession : IAsyncDisposable
{
	/// <summary>Gets the stable tab identifier.</summary>
	Guid Id { get; }

	/// <summary>Gets the owned pane sessions.</summary>
	IReadOnlyList<IPaneSession> Panes { get; }

	/// <summary>Gets the active pane session.</summary>
	IPaneSession? ActivePane { get; }

	/// <summary>Gets the current split orientation.</summary>
	PaneSplitOrientation SplitOrientation { get; }

	/// <summary>Gets whether the active content can create a split pane.</summary>
	bool CanSplit { get; }

	/// <summary>Gets whether every pane can capture independently restorable content.</summary>
	bool CanRestore { get; }

	/// <summary>Occurs when the pane collection changes.</summary>
	event EventHandler? PanesChanged;

	/// <summary>Occurs when the active pane changes.</summary>
	event EventHandler? ActivePaneChanged;

	/// <summary>Occurs when the split orientation changes.</summary>
	event EventHandler? SplitOrientationChanged;

	/// <summary>Opens and activates a second pane using the content's split policy.</summary>
	/// <param name="orientation">The split orientation.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The created pane session.</returns>
	ValueTask<IPaneSession> OpenSplitAsync(PaneSplitOrientation orientation, CancellationToken cancellationToken = default);

	/// <summary>Opens and activates a second pane.</summary>
	/// <param name="orientation">The split orientation.</param>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created pane session.</returns>
	ValueTask<IPaneSession> OpenSplitAsync(PaneSplitOrientation orientation, BrowseLocation? initialLocation, CancellationToken cancellationToken = default);

	/// <summary>Closes and disposes a pane.</summary>
	/// <param name="paneId">The pane identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the pane could be closed.</returns>
	ValueTask<bool> ClosePaneAsync(Guid paneId, CancellationToken cancellationToken = default);

	/// <summary>Activates a pane.</summary>
	/// <param name="paneId">The pane identifier.</param>
	/// <returns><see langword="true"/> when the pane exists.</returns>
	bool SetActivePane(Guid paneId);

	/// <summary>Changes the orientation of an existing split.</summary>
	/// <param name="orientation">The new split orientation.</param>
	/// <returns><see langword="true"/> when the tab is split.</returns>
	bool SetSplitOrientation(PaneSplitOrientation orientation);

	/// <summary>Captures every pane and the layout as one tab restoration state.</summary>
	/// <returns>The captured state, or null if any content cannot be restored.</returns>
	TabRestoreState? CaptureRestoreState();
}

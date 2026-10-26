// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>Captures all pane content, layout, and active-pane state without retaining live models.</summary>
public sealed class TabRestoreState
{
	internal IReadOnlyList<Func<IPaneContentSession>> ContentFactories { get; }

	/// <summary>Gets the number of captured panes.</summary>
	public int PaneCount => ContentFactories.Count;

	/// <summary>Gets the captured active-pane index.</summary>
	public int ActivePaneIndex { get; }

	/// <summary>Gets the captured split orientation.</summary>
	public PaneSplitOrientation SplitOrientation { get; }

	internal TabRestoreState(IEnumerable<Func<IPaneContentSession>> factories, int activePaneIndex, PaneSplitOrientation splitOrientation)
	{
		ContentFactories = Array.AsReadOnly(factories.ToArray());
		ActivePaneIndex = activePaneIndex;
		SplitOrientation = splitOrientation;
	}
}

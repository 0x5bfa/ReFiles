// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to BrowseNavigationHistory state and operations.</summary>
public interface IBrowseNavigationHistory
{
	/// <summary>Gets the committed locations.</summary>
	IReadOnlyList<BrowseLocation> Entries { get; }

	/// <summary>Gets the index of the current location.</summary>
	int CurrentIndex { get; }

	/// <summary>Gets the current location.</summary>
	BrowseLocation? Current { get; }

	/// <summary>Gets a value indicating whether a previous location exists.</summary>
	bool CanGoBack { get; }

	/// <summary>Gets a value indicating whether a following location exists.</summary>
	bool CanGoForward { get; }

	/// <summary>Occurs when the committed history changes.</summary>
	event EventHandler? Changed;

	/// <summary>Captures the immutable state of the navigation history.</summary>
	/// <returns>The captured state.</returns>
	BrowseNavigationHistoryState CaptureState();
}

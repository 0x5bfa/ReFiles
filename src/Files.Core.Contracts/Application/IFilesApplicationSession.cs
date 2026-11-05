// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Browsing;

namespace Files.Core.Sessions;

/// <summary>Provides consumer access to FilesApplicationSession state and operations.</summary>
public interface IFilesApplicationSession : IAsyncDisposable
{
	/// <summary>Gets the owned window sessions.</summary>
	IReadOnlyList<IWindowSession> Windows { get; }

	/// <summary>Gets the window most recently activated by a host.</summary>
	IWindowSession? ActiveWindow { get; }

	/// <summary>Occurs when the window collection changes.</summary>
	event EventHandler? WindowsChanged;

	/// <summary>Occurs when the active window changes.</summary>
	event EventHandler? ActiveWindowChanged;

	/// <summary>Creates and owns a window session.</summary>
	/// <param name="initialLocation">The optional initial browse location.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created window session.</returns>
	ValueTask<IWindowSession> CreateWindowAsync(BrowseLocation? initialLocation = null, CancellationToken cancellationToken = default);

	/// <summary>Closes and disposes a window session.</summary>
	/// <param name="windowId">The window identifier.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns><see langword="true"/> when the window existed.</returns>
	ValueTask<bool> CloseWindowAsync(Guid windowId, CancellationToken cancellationToken = default);

	/// <summary>Records host activation for a window.</summary>
	/// <param name="windowId">The activated window identifier.</param>
	/// <returns><see langword="true"/> when the window exists.</returns>
	bool SetActiveWindow(Guid windowId);
}

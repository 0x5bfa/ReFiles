// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Discovers and invokes contextual Shell commands for a captured selection.</summary>
public interface IWindowsShellContextualCommandService
{
	/// <summary>
	/// Gets the contextual Shell commands applicable to a location and selection.
	/// </summary>
	/// <param name="location">The current folder reference, when the view has one.</param>
	/// <param name="selection">The selected item references.</param>
	/// <param name="ownerWindowHandle">The owner window used when constructing folder background commands.</param>
	/// <param name="cancellationToken">The token used to cancel command discovery.</param>
	/// <returns>Apartment-neutral command descriptions keyed by stable Shell command identifiers.</returns>
	Task<IReadOnlyList<WindowsShellContextualCommand>> GetCommandsAsync(StorableReference? location, IReadOnlyList<StorableReference> selection, nint ownerWindowHandle,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Invokes a contextual Shell command against the current location and selection.
	/// </summary>
	/// <param name="location">The current folder reference, when the view has one.</param>
	/// <param name="selection">The selected item references.</param>
	/// <param name="command">The previously resolved command description.</param>
	/// <param name="context">The owner window and working-directory context.</param>
	/// <param name="cancellationToken">The token used to cancel command preparation.</param>
	/// <returns><see langword="true"/> when the Shell command was invoked.</returns>
	Task<bool> InvokeAsync(StorableReference? location, IReadOnlyList<StorableReference> selection, WindowsShellContextualCommand command, WindowsShellInvocationContext context,
		CancellationToken cancellationToken = default);
}

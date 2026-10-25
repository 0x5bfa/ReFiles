// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Windows;

/// <summary>Executes Windows Shell intents through the configured host.</summary>
public interface IWindowsShellOperationService
{
	/// <summary>Gets the Shell New commands for a folder.</summary>
	/// <param name="folderPath">The folder path.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The available commands.</returns>
	Task<IReadOnlyList<WindowsShellNewItem>> GetShellNewItemsAsync(string folderPath, CancellationToken cancellationToken = default);

	/// <summary>Submits a Shell New command.</summary>
	/// <param name="folderPath">The folder path.</param>
	/// <param name="commandOffset">The Shell command offset.</param>
	/// <param name="name">The command name.</param>
	/// <param name="cancellationToken">The admission cancellation token.</param>
	/// <returns>The operation completion task.</returns>
	Task InvokeNewItemAsync(string folderPath, uint commandOffset, string name, CancellationToken cancellationToken);

	/// <summary>Transfers paths obtained from a native clipboard.</summary>
	/// <param name="paths">The complete source paths.</param>
	/// <param name="destination">The destination reference.</param>
	/// <param name="move">Whether to move instead of copy.</param>
	/// <param name="cancellationToken">The admission cancellation token.</param>
	/// <returns>The operation completion task.</returns>
	Task TransferPathsAsync(IReadOnlyList<string> paths, StorableReference destination, bool move, CancellationToken cancellationToken);
}

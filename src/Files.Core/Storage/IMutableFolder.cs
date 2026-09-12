// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Represents a folder whose contents can change.
/// </summary>
public interface IMutableFolder : IFolder
{
	/// <summary>
	/// Creates a watcher for changes in this folder.
	/// </summary>
	/// <param name="cancellationToken">The token used to cancel watcher creation.</param>
	/// <returns>A disposable folder watcher.</returns>
	Task<IFolderWatcher> GetFolderWatcherAsync(CancellationToken cancellationToken = default);
}

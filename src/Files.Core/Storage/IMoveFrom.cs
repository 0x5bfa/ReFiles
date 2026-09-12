// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Provides an optimized move implementation for a writable destination folder.
/// </summary>
public interface IMoveFrom : IModifiableFolder
{
	/// <summary>
	/// Moves a file from a source folder into this folder.
	/// </summary>
	/// <param name="fileToMove">The file to move.</param>
	/// <param name="source">The source folder.</param>
	/// <param name="overwrite">Whether an existing destination may be replaced.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="fallback">The fallback move operation.</param>
	/// <returns>The moved child file.</returns>
	/// <exception cref="FileNotFoundException">The source file was not found.</exception>
	Task<IChildFile> MoveFromAsync(IChildFile fileToMove, IModifiableFolder source, bool overwrite, CancellationToken cancellationToken, MoveFromDelegate fallback);
}

/// <summary>
/// Represents a fallback move operation.
/// </summary>
/// <param name="destination">The destination folder.</param>
/// <param name="file">The file to move.</param>
/// <param name="source">The source folder.</param>
/// <param name="overwrite">Whether an existing destination may be replaced.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
/// <returns>The moved child file.</returns>
public delegate Task<IChildFile> MoveFromDelegate(IModifiableFolder destination, IChildFile file, IModifiableFolder source, bool overwrite, CancellationToken cancellationToken);

/// <summary>
/// Provides an optimized renamed move implementation for a writable destination folder.
/// </summary>
public interface IMoveRenamedFrom : IMoveFrom
{
	/// <summary>
	/// Moves a file from a source folder into this folder with a new name.
	/// </summary>
	/// <param name="fileToMove">The file to move.</param>
	/// <param name="source">The source folder.</param>
	/// <param name="overwrite">Whether an existing destination may be replaced.</param>
	/// <param name="newName">The destination name.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="fallback">The fallback move operation.</param>
	/// <returns>The moved child file.</returns>
	Task<IChildFile> MoveFromAsync(IChildFile fileToMove, IModifiableFolder source, bool overwrite, string newName, CancellationToken cancellationToken, MoveRenamedFromDelegate fallback);
}

/// <summary>
/// Represents a fallback move operation that assigns a new name.
/// </summary>
/// <param name="destination">The destination folder.</param>
/// <param name="file">The file to move.</param>
/// <param name="source">The source folder.</param>
/// <param name="overwrite">Whether an existing destination may be replaced.</param>
/// <param name="newName">The destination name.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
/// <returns>The moved child file.</returns>
public delegate Task<IChildFile> MoveRenamedFromDelegate(IModifiableFolder destination, IChildFile file, IModifiableFolder source, bool overwrite, string newName, CancellationToken cancellationToken);

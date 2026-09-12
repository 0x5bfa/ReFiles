// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Provides an optimized file-copy implementation for a writable folder.
/// </summary>
public interface ICreateCopyOf : IModifiableFolder
{
	/// <summary>
	/// Creates a copy of a file in this folder.
	/// </summary>
	/// <param name="fileToCopy">The file to copy.</param>
	/// <param name="overwrite">Whether an existing destination may be replaced.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="fallback">The fallback copy operation.</param>
	/// <returns>The created child file.</returns>
	/// <exception cref="FileNotFoundException">The source file was not found.</exception>
	Task<IChildFile> CreateCopyOfAsync(IFile fileToCopy, bool overwrite, CancellationToken cancellationToken, CreateCopyOfDelegate fallback);
}

/// <summary>
/// Represents a fallback copy operation.
/// </summary>
/// <param name="destination">The destination folder.</param>
/// <param name="fileToCopy">The file to copy.</param>
/// <param name="overwrite">Whether an existing destination may be replaced.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
/// <returns>The created child file.</returns>
public delegate Task<IChildFile> CreateCopyOfDelegate(IModifiableFolder destination, IFile fileToCopy, bool overwrite, CancellationToken cancellationToken);

/// <summary>
/// Provides an optimized renamed file-copy implementation for a writable folder.
/// </summary>
public interface ICreateRenamedCopyOf : ICreateCopyOf
{
	/// <summary>
	/// Creates a copy of a file in this folder with a new name.
	/// </summary>
	/// <param name="fileToCopy">The file to copy.</param>
	/// <param name="overwrite">Whether an existing destination may be replaced.</param>
	/// <param name="newName">The destination name.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <param name="fallback">The fallback copy operation.</param>
	/// <returns>The created child file.</returns>
	Task<IChildFile> CreateCopyOfAsync(IFile fileToCopy, bool overwrite, string newName, CancellationToken cancellationToken, CreateRenamedCopyOfDelegate fallback);
}

/// <summary>
/// Represents a fallback copy operation that assigns a new name.
/// </summary>
/// <param name="destination">The destination folder.</param>
/// <param name="fileToCopy">The file to copy.</param>
/// <param name="overwrite">Whether an existing destination may be replaced.</param>
/// <param name="newName">The destination name.</param>
/// <param name="cancellationToken">The token used to cancel the operation.</param>
/// <returns>The created child file.</returns>
public delegate Task<IChildFile> CreateRenamedCopyOfDelegate(IModifiableFolder destination, IFile fileToCopy, bool overwrite, string newName, CancellationToken cancellationToken);

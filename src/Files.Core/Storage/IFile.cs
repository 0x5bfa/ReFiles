// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Core.Storage;

/// <summary>
/// Represents a readable or writable storage file.
/// </summary>
public interface IFile : IStorable
{
	/// <summary>
	/// Opens a stream to the file with the requested access mode.
	/// </summary>
	/// <param name="accessMode">The operations that can be performed on the file.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A stream that provides access to the file.</returns>
	Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Represents a file exposed by the Windows Shell.</summary>
public sealed class WindowsFile : WindowsStorable, IFile
{
	internal WindowsFile(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	/// <summary>Opens an owned stream for reading this Windows file.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The stream owned by the caller. Keep the storage source alive until the stream is disposed.</returns>
	public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
	{
		return Factory.OpenStreamAsync(Descriptor, FileAccess.Read, cancellationToken);
	}

	/// <summary>Opens an owned write stream to the existing file without truncating its content.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The stream owned by the caller. Keep the storage source alive until the stream is disposed.</returns>
	public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
	{
		return Factory.OpenStreamAsync(Descriptor, FileAccess.Write, cancellationToken);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Windows;

/// <summary>Represents a file exposed by the Windows Shell.</summary>
public sealed class WindowsFile : WindowsStorable, IChildFile
{
	internal WindowsFile(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	/// <summary>Opens the Windows item for the requested access mode.</summary>
	/// <param name="accessMode">The requested access mode.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The opened stream.</returns>
	public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return await Factory.OpenStreamAsync(Descriptor, accessMode, cancellationToken).ConfigureAwait(false);
	}
}

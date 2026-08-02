// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

public sealed class WindowsFile : WindowsStorable, IChildFile
{
	internal WindowsFile(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	public async Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return await Factory.OpenStreamAsync(Descriptor, accessMode, cancellationToken).ConfigureAwait(false);
	}
}

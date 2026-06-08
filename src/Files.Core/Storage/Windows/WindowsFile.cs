// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

public sealed class WindowsFile : WindowsStorable, IChildFile
{
	internal WindowsFile(
		WindowsStorableDescriptor descriptor,
		WindowsStorableFactory factory)
		: base(descriptor, factory)
	{
	}

	public async Task<Stream> OpenStreamAsync(
		FileAccess accessMode,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (FileSystemPath is { } fileSystemPath)
		{
			return new FileStream(fileSystemPath, new FileStreamOptions
			{
				Mode = FileMode.Open,
				Access = accessMode,
				Share = FileShare.ReadWrite | FileShare.Delete,
				Options = FileOptions.Asynchronous,
			});
		}

		if (accessMode is not FileAccess.Read)
		{
			throw new UnauthorizedAccessException(
				"The virtual Shell item does not expose a writable file-system path.");
		}

		return await Factory.OpenReadStreamAsync(Descriptor, cancellationToken).ConfigureAwait(false);
	}
}

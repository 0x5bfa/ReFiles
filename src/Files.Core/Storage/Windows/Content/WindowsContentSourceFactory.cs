// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Windows;

/// <summary>Exposes supported Windows content access independently of file and folder classification.</summary>
public sealed class WindowsContentSourceFactory : ICapabilityFactory<IReadableContent>, ICapabilityFactory<IWritableContent>
{
	/// <summary>Creates read access for file-system files and Shell items marked as streams, including archive folders.</summary>
	/// <param name="context">The borrowed item context.</param>
	/// <returns>The item capability, or <see langword="null"/> when stream access is unsupported.</returns>
	public IReadableContent? Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource source && context.CoreModel is WindowsStorable item && (item.IsStream || item is WindowsFile { IsFileSystem: true })
			? new WindowsContentSource(item.Descriptor, source.Scheduler, source.ItemIdReader) : null;
	}

	/// <summary>Creates write access for existing file-system files without inferring write support for virtual Shell streams.</summary>
	/// <param name="context">The borrowed item context.</param>
	/// <returns>The item capability, or <see langword="null"/> when write access is unsupported.</returns>
	IWritableContent? ICapabilityFactory<IWritableContent>.Create(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource source && context.CoreModel is WindowsFile { IsFileSystem: true } file
			? new WritableContent(new WindowsContentSource(file.Descriptor, source.Scheduler, source.ItemIdReader)) : null;
	}

	private sealed record WritableContent(WindowsContentSource Content) : IWritableContent
	{
		public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
		{
			return Content.OpenWriteAsync(cancellationToken);
		}
	}
}

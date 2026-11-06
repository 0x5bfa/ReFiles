// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Capabilities.Content;
using Files.Core.Storage.Archives;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

/// <summary>Opens Windows content while keeping virtual streams on their owning STA lane.</summary>
internal sealed class WindowsContentSource : IReadableContent, IWritableContent, IArchiveInput
{
	private readonly WindowsStorableDescriptor _descriptor;
	private readonly IWindowsShellScheduler _scheduler;
	private readonly IWindowsItemIdReader _itemIdReader;

	internal WindowsContentSource(WindowsStorableDescriptor descriptor, IWindowsShellScheduler scheduler, IWindowsItemIdReader itemIdReader)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(itemIdReader);

		_descriptor = descriptor;
		_scheduler = scheduler;
		_itemIdReader = itemIdReader;
	}

	public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
	{
		return OpenStreamAsync(FileAccess.Read, cancellationToken);
	}

	Task<Stream> IWritableContent.OpenWriteAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_descriptor.Metadata.FileSystemPath is null || _descriptor.Metadata.IsFolder)
		{
			throw new NotSupportedException("Write access requires a file-system file.");
		}

		return OpenStreamAsync(FileAccess.Write, cancellationToken);
	}

	Task<Stream> IArchiveInput.OpenReadAsync(CancellationToken cancellationToken)
	{
		return _descriptor.Metadata.FileSystemPath is null ? ArchiveInputStreams.MaterializeAsync(this, cancellationToken) : OpenArchiveInputAsync(cancellationToken);
	}

	private Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_descriptor.Metadata.FileSystemPath is { } fileSystemPath)
		{
			return Task.FromResult<Stream>(OpenFileSystemStream(fileSystemPath, accessMode, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous | FileOptions.SequentialScan));
		}

		return _scheduler.InvokeAsync<Stream>(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(_descriptor.Locator);
			if (shellItem is null)
			{
				return null!;
			}

			var hr = PInvoke.CreateBindCtx(0, out var bindContext).ThrowOnFailure();
			if (bindContext is null)
			{
				throw new IOException("Could not create a Shell bind context.");
			}

			BIND_OPTS bindOptions = default;
			bindOptions.cbStruct = (uint)sizeof(BIND_OPTS);
			bindOptions.grfMode = accessMode switch
			{
				FileAccess.Read => (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE),
				FileAccess.Write => (uint)(STGM.STGM_WRITE | STGM.STGM_SHARE_DENY_WRITE),
				FileAccess.ReadWrite => (uint)(STGM.STGM_READWRITE | STGM.STGM_SHARE_DENY_WRITE),
				_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
			};

			hr = bindContext.SetBindOptions(in bindOptions).ThrowOnFailure();
			hr = shellItem.BindToHandler<IStream>(bindContext, PInvoke.BHID_Stream, out var shellStream).ThrowOnFailure();
			if (shellStream is null)
			{
				throw new IOException("The Shell item returned no stream.");
			}

			return new ShellReadStream(_scheduler, shellStream, accessMode);
		}, cancellationToken);
	}

	private Task<Stream> OpenArchiveInputAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var path = _descriptor.Metadata.FileSystemPath ?? throw new NotSupportedException("Pinned archive input requires a file-system item.");
		return Task.FromResult<Stream>(OpenFileSystemStream(path, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous | FileOptions.RandomAccess));
	}

	private Stream OpenFileSystemStream(string fileSystemPath, FileAccess accessMode, FileShare share, FileOptions options)
	{
		if (accessMode is not (FileAccess.Read or FileAccess.Write or FileAccess.ReadWrite))
		{
			throw new ArgumentOutOfRangeException(nameof(accessMode));
		}

		var accessRights = accessMode switch
		{
			FileAccess.Read => (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ,
			FileAccess.Write => (uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE | FILE_ACCESS_RIGHTS.FILE_READ_ATTRIBUTES),
			FileAccess.ReadWrite => (uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_READ | FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE),
			_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
		};
		var flags = FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED;
		if (options.HasFlag(FileOptions.SequentialScan))
		{
			flags |= FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_SEQUENTIAL_SCAN;
		}

		if (options.HasFlag(FileOptions.RandomAccess))
		{
			flags |= FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_RANDOM_ACCESS;
		}

		var handle = PInvoke.CreateFile(fileSystemPath, accessRights, (FILE_SHARE_MODE)share, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, flags, null);
		if (handle.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		FileStream stream;
		try
		{
			stream = new FileStream(handle, accessMode, bufferSize: 4096, isAsync: true);
		}
		catch
		{
			handle.Dispose();
			throw;
		}

		try
		{
			var actualIdentity = _itemIdReader.GetFileSystemIdentity(stream.SafeFileHandle);
			if (_descriptor.FileSystemIdentity is { } expectedIdentity)
			{
				if (actualIdentity is null || !StringComparer.Ordinal.Equals(expectedIdentity, actualIdentity))
				{
					throw new FileNotFoundException("The filesystem item was replaced before its stream could be opened.", fileSystemPath);
				}
			}
			else if (_itemIdReader.GetItemId(_descriptor.Locator.ParsingName, fileSystemPath, out _) != _descriptor.ItemId)
			{
				throw new FileNotFoundException("The filesystem item was replaced before its stream could be opened.", fileSystemPath);
			}

			return stream;
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}
}

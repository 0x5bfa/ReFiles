// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using Files.Core.Capabilities.Content;
using Files.Core.Storage.Archives;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

/// <summary>Opens Windows content while keeping virtual streams on their owning STA lane.</summary>
internal sealed class WindowsContentSource : IReadableContent, IArchiveInput
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

	internal Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
	{
		return OpenStreamAsync(FileAccess.Write, cancellationToken);
	}

	Task<Stream> IArchiveInput.OpenReadAsync(CancellationToken cancellationToken)
	{
		return _descriptor.Metadata.FileSystemPath is null ? ArchiveStreamResolver.MaterializeAsync(this, cancellationToken) : OpenArchiveInputAsync(cancellationToken);
	}

	private Task<Stream> OpenStreamAsync(FileAccess accessMode, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_descriptor.Metadata.FileSystemPath is { } fileSystemPath)
		{
			return Task.FromResult<Stream>(OpenFileSystemStream(fileSystemPath, accessMode));
		}

		return _scheduler.InvokeAsync<Stream>(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(_descriptor.Locator);
			if (shellItem is null)
			{
				return null!;
			}

			var hr = PInvoke.CreateBindCtx(0, out IBindCtx? bindContext);
			hr.ThrowOnFailure();

			if (bindContext is null)
			{
				throw new IOException("Could not create a Shell bind context.");
			}

			BIND_OPTS bindOptions = default;
			bindOptions.cbStruct = (uint)Unsafe.SizeOf<BIND_OPTS>();
			bindOptions.grfMode = accessMode switch
			{
				FileAccess.Read => (uint)(STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE),
				FileAccess.Write => (uint)(STGM.STGM_WRITE | STGM.STGM_SHARE_DENY_WRITE),
				FileAccess.ReadWrite => (uint)(STGM.STGM_READWRITE | STGM.STGM_SHARE_DENY_WRITE),
				_ => throw new ArgumentOutOfRangeException(nameof(accessMode)),
			};
			hr = bindContext.SetBindOptions(in bindOptions);
			hr.ThrowOnFailure();

			hr = shellItem.BindToHandler(bindContext, PInvoke.BHID_Stream, out IStream? shellStream);
			hr.ThrowOnFailure();

			if (shellStream is null)
			{
				throw new IOException("The Shell item returned no stream.");
			}

			return new ShellReadStream(_scheduler, shellStream, accessMode);
		},
		cancellationToken);
	}

	private Task<Stream> OpenArchiveInputAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var path = _descriptor.Metadata.FileSystemPath ?? throw new NotSupportedException("Pinned archive input requires a file-system item.");
		var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
		try
		{
			if (_itemIdReader.GetItemId(_descriptor.Locator.ParsingName, path) != _descriptor.ItemId)
			{
				throw new FileNotFoundException("The archive item was replaced before its input could be opened.", path);
			}

			return Task.FromResult<Stream>(input);
		}
		catch
		{
			input.Dispose();
			throw;
		}
	}

	private static Stream OpenFileSystemStream(string fileSystemPath, FileAccess accessMode)
	{
		if (accessMode is not (FileAccess.Read or FileAccess.Write or FileAccess.ReadWrite))
		{
			throw new ArgumentOutOfRangeException(nameof(accessMode));
		}

		return new FileStream(fileSystemPath, new FileStreamOptions
		{
			Access = accessMode,
			Mode = FileMode.Open,
			Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
			Share = FileShare.ReadWrite | FileShare.Delete,
		});
	}
}

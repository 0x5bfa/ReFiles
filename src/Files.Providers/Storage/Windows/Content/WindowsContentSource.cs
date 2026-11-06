// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Files.Core.Capabilities.Content;
using Files.Core.Storage.Archives;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

/// <summary>Opens Windows content through Shell streams on their owning STA lane.</summary>
internal sealed class WindowsContentSource : IReadableContent, IWritableContent, IArchiveInput
{
	private readonly WindowsStorableDescriptor _descriptor;
	private readonly IWindowsShellScheduler _scheduler;

	internal WindowsContentSource(WindowsStorableDescriptor descriptor, IWindowsShellScheduler scheduler)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(scheduler);

		_descriptor = descriptor;
		_scheduler = scheduler;
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

		return _scheduler.InvokeAsync<Stream>(() =>
		{
			var shellItem = WindowShellHelpers.TryResolveItem(_descriptor.Locator);
			if (shellItem is null)
			{
				throw new FileNotFoundException("The Shell item could not be resolved.", _descriptor.Locator.ParsingName);
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
		return OpenStreamAsync(FileAccess.Read, cancellationToken);
	}
}

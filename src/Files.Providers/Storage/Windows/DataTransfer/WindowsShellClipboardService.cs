// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;
using global::Windows.ApplicationModel.DataTransfer;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Places Windows Shell selections on the OLE clipboard without converting them to physical paths.
/// </summary>
public sealed class WindowsShellClipboardService : IWindowsShellClipboardService
{
	private readonly WindowsStorageSource _source;

	private IDataObject? _clipboardDataObject;
	private volatile bool _isDisposed;
	private long _setGeneration;

	internal WindowsShellClipboardService(WindowsStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	/// <summary>Places a Windows Shell selection on the OLE clipboard.</summary>
	/// <param name="selection">The selected Windows Shell item references.</param>
	/// <param name="move"><see langword="true"/> to advertise a cut operation; otherwise, to advertise copy and link operations.</param>
	/// <param name="ownerWindowHandle">The optional handle of the window that owns the selection.</param>
	/// <param name="cancellationToken">The token used to cancel selection resolution or queued Shell work.</param>
	/// <returns>A task that represents the clipboard operation.</returns>
	public async Task SetItemsAsync(IReadOnlyList<StorableReference> selection, bool move, nint ownerWindowHandle = default, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(selection);

		ObjectDisposedException.ThrowIf(_isDisposed, _source);

		if (selection.Count is 0)
		{
			throw new ArgumentException("A clipboard selection cannot be empty.", nameof(selection));
		}

		var generation = Interlocked.Increment(ref _setGeneration);
		var locators = await WindowsShellSelectionResolver.ResolveAsync(_source, selection, cancellationToken).ConfigureAwait(false);
		await _source.Scheduler.InvokeAsync(() => SetItemsOnCurrentSta(locators, move, (HWND)ownerWindowHandle, generation), cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public bool HasStorageItems()
	{
		try
		{
			if (Clipboard.GetContent().Contains(StandardDataFormats.StorageItems))
			{
				return true;
			}
		}
		catch
		{
		}

		try
		{
			return TryGetNativeClipboardFilePaths(out _);
		}
		catch
		{
			return false;
		}
	}

	/// <inheritdoc />
#pragma warning disable RS0030 // WinRT storage projection is required for native clipboard interoperability.
	public async Task<WindowsShellClipboardSnapshot?> ReadFileSystemItemsAsync(CancellationToken cancellationToken = default)
	{
		DataPackageView? data = null;
		var paths = new List<string>();
		try
		{
			data = Clipboard.GetContent();
			if (data.Contains(StandardDataFormats.StorageItems))
			{
				IReadOnlyList<global::Windows.Storage.IStorageItem> storageItems;
				try
				{
					storageItems = await data.GetStorageItemsAsync();
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch
				{
					storageItems = [];
				}

				foreach (var storageItem in storageItems)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (!string.IsNullOrWhiteSpace(storageItem.Path))
						{
							paths.Add(storageItem.Path);
						}
					}
					catch
					{
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			data = null;
		}

		IReadOnlyList<string> nativePaths = [];
		var hasNativePaths = paths.Count is not 0 || TryGetNativeClipboardFilePaths(out nativePaths);
		if (!hasNativePaths)
		{
			return null;
		}

		if (paths.Count is 0)
		{
			paths.AddRange(nativePaths);
		}

		if (paths.Count is 0)
		{
			return null;
		}

		var move = data?.RequestedOperation.HasFlag(DataPackageOperation.Move) is true;

		return new WindowsShellClipboardSnapshot(Array.AsReadOnly(paths.ToArray()), move);
	}

#pragma warning restore RS0030

	internal Task FlushAsync()
	{
		return _source.Scheduler.InvokeAsync(FlushOnCurrentSta);
	}

	private static unsafe bool TryGetNativeClipboardFilePaths(out IReadOnlyList<string> paths)
	{
		paths = [];
		if (PInvoke.OleGetClipboard(out var dataObject).Failed || dataObject is null)
		{
			return false;
		}

		var format = default(FORMATETC);
		format.cfFormat = 15;
		format.dwAspect = (uint)DVASPECT.DVASPECT_CONTENT;
		format.lindex = -1;
		format.tymed = (uint)TYMED.TYMED_HGLOBAL;
		if (dataObject.GetData(in format, out var medium).Failed)
		{
			return false;
		}

		try
		{
			if (medium.tymed is not TYMED.TYMED_HGLOBAL || medium.u.hGlobal.IsNull)
			{
				return false;
			}

			var size = PInvoke.GlobalSize(medium.u.hGlobal);
			if (size < 20)
			{
				return false;
			}

			var buffer = PInvoke.GlobalLock(medium.u.hGlobal);
			if (buffer is null)
			{
				return false;
			}

			try
			{
				var offset = Marshal.ReadInt32((nint)buffer);
				var isWide = Marshal.ReadInt32((nint)buffer, 16) is not 0;
				if (offset < 20 || (nuint)offset >= size || !isWide)
				{
					return false;
				}

				var remaining = checked((int)size - offset);
				var current = (nint)buffer + offset;
				var result = new List<string>();
				while (remaining >= sizeof(char))
				{
					var length = 0;
					while ((length + 1) * sizeof(char) <= remaining && Marshal.ReadInt16(current, length * sizeof(char)) is not 0)
					{
						length++;
					}

					if (length is 0)
					{
						break;
					}

					result.Add(Marshal.PtrToStringUni(current, length));
					var advance = checked((length + 1) * sizeof(char));
					current += advance;
					remaining -= advance;
				}

				paths = result;

				return result.Count is not 0;
			}
			finally
			{
				_ = PInvoke.GlobalUnlock(medium.u.hGlobal);
			}
		}
		finally
		{
			PInvoke.ReleaseStgMedium(ref medium);
		}
	}

	private bool SetItemsOnCurrentSta(IReadOnlyList<WindowsItemLocator> locators, bool move, HWND ownerWindow, long generation)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, _source);

		if (generation != Volatile.Read(ref _setGeneration))
		{
			return false;
		}

		var dataObject = WindowsShellDataObjectFactory.Create(locators, ownerWindow);
		var dropEffect = move ? DROPEFFECT.DROPEFFECT_MOVE : DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_LINK;
		WindowsShellDataObjectFormat.SetDword(dataObject, WindowsShellDataObjectFormat.PreferredDropEffect, (uint)dropEffect);
		try
		{
			PInvoke.OleSetClipboard(dataObject).ThrowOnFailure();
			_clipboardDataObject = dataObject;
		}
		catch
		{
			throw;
		}

		return true;
	}

	private bool FlushOnCurrentSta()
	{
		_isDisposed = true;
		Interlocked.Increment(ref _setGeneration);
		var dataObject = _clipboardDataObject;
		_clipboardDataObject = null;
		if (dataObject is not null && PInvoke.OleIsCurrentClipboard(dataObject) == HRESULT.S_OK)
		{
			PInvoke.OleFlushClipboard().ThrowOnFailure();
		}

		return true;
	}
}

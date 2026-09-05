// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>
/// Places Windows Shell selections on the OLE clipboard without converting them to physical paths.
/// </summary>
public sealed class WindowsShellClipboardService
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

	internal Task FlushAsync()
	{
		return _source.Scheduler.InvokeAsync(FlushOnCurrentSta);
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
		var asyncCapability = TryStartAsyncOperation(dataObject);
		try
		{
			if (asyncCapability is not null)
			{
				WindowsShellDataObjectFormat.SetDword(dataObject, WindowsShellDataObjectFormat.AsyncFlag, 1);
			}

			PInvoke.OleSetClipboard(dataObject).ThrowOnFailure();
			_clipboardDataObject = dataObject;
		}
		catch
		{
			asyncCapability?.EndOperation(HRESULT.E_FAIL, null!, (uint)DROPEFFECT.DROPEFFECT_NONE);

			throw;
		}

		return true;
	}

	private static IDataObjectAsyncCapability? TryStartAsyncOperation(IDataObject dataObject)
	{
		if (dataObject is not IDataObjectAsyncCapability asyncCapability || asyncCapability.GetAsyncMode(out var isAsync).Failed || !isAsync)
		{
			return null;
		}

		return asyncCapability.StartOperation(null!).Succeeded ? asyncCapability : null;
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

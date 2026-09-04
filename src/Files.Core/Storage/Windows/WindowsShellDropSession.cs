// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Forwards one WinUI drag sequence to a native Windows Shell drop target.
/// </summary>
public sealed class WindowsShellDropSession : IDisposable
{
	private const WindowsShellDropEffects TransferEffects = WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link;

	private readonly IDataObject _dataObject;
	private readonly IDropTarget _dropTarget;
	private readonly int _ownerThreadId;

	private bool _isDisposed;
	private bool _isEntered;

	internal WindowsShellDropSession(IDropTarget dropTarget, IDataObject dataObject)
	{
		ArgumentNullException.ThrowIfNull(dropTarget);

		ArgumentNullException.ThrowIfNull(dataObject);

		_dropTarget = dropTarget;
		_dataObject = dataObject;
		_ownerThreadId = Environment.CurrentManagedThreadId;
	}

	/// <summary>Tries to forward the initial drag entry to the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <param name="acceptedEffect">The operation currently accepted by the Shell target.</param>
	/// <returns><see langword="true"/> when the Shell target accepted the drag sequence, even if it does not currently accept an operation.</returns>
	public bool TryDragEnter(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects, out WindowsShellDropEffects acceptedEffect)
	{
		VerifyAccess();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		acceptedEffect = WindowsShellDropEffects.None;

		if (_isEntered)
		{
			throw new InvalidOperationException("The Shell drop session has already received DragEnter.");
		}

		var effect = ToNativeEffects(allowedEffects);
		var point = ToNativePoint(screenPoint);
		var hr = _dropTarget.DragEnter(_dataObject, (MODIFIERKEYS_FLAGS)(uint)modifiers, point, ref effect);
		if (hr.Failed)
		{
			return false;
		}

		_isEntered = true;
		acceptedEffect = ToPublicEffects(effect);

		return true;
	}

	/// <summary>Forwards pointer movement within the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <returns>The operation accepted by the Shell target.</returns>
	public WindowsShellDropEffects DragOver(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects)
	{
		VerifyEntered();

		var effect = ToNativeEffects(allowedEffects);
		var point = ToNativePoint(screenPoint);
		if (_dropTarget.DragOver((MODIFIERKEYS_FLAGS)(uint)modifiers, point, ref effect).Failed)
		{
			return WindowsShellDropEffects.None;
		}

		return ToPublicEffects(effect);
	}

	/// <summary>Notifies the Shell target that the pointer left it.</summary>
	public void DragLeave()
	{
		VerifyAccess();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_isEntered)
		{
			return;
		}

		_isEntered = false;
		_ = _dropTarget.DragLeave();
	}

	/// <summary>Forwards the completed drop to the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <returns>The operation performed by the Shell target.</returns>
	public WindowsShellDropEffects Drop(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects)
	{
		VerifyEntered();

		var effect = ToNativeEffects(allowedEffects);
		var point = ToNativePoint(screenPoint);
		_isEntered = false;
		if (_dropTarget.Drop(_dataObject, (MODIFIERKEYS_FLAGS)(uint)modifiers, point, ref effect).Failed)
		{
			return WindowsShellDropEffects.None;
		}

		return ToPublicEffects(effect);
	}

	/// <summary>Ends target feedback when the drag sequence did not complete.</summary>
	public void Dispose()
	{
		VerifyAccess();
		if (_isDisposed)
		{
			return;
		}

		var notifyTarget = _isEntered;
		_isEntered = false;
		_isDisposed = true;
		if (notifyTarget)
		{
			_ = _dropTarget.DragLeave();
		}
	}

	private static DROPEFFECT ToNativeEffects(WindowsShellDropEffects effects) => (DROPEFFECT)(uint)(effects & TransferEffects);

	private static POINTL ToNativePoint(Point point)
	{
		var result = default(POINTL);
		result.x = point.X;
		result.y = point.Y;

		return result;
	}

	private static WindowsShellDropEffects ToPublicEffects(DROPEFFECT effects) => (WindowsShellDropEffects)((uint)effects & (uint)TransferEffects);

	private void VerifyAccess()
	{
		if (Environment.CurrentManagedThreadId != _ownerThreadId)
		{
			throw new InvalidOperationException("The Shell drop session must be used on the STA thread that created it.");
		}
	}

	private void VerifyEntered()
	{
		VerifyAccess();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_isEntered)
		{
			throw new InvalidOperationException("The Shell drop session has not received DragEnter.");
		}
	}
}

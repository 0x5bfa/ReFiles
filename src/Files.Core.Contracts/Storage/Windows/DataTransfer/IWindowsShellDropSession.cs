// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using System.Drawing;

namespace Files.Core.Storage.Windows;

/// <summary>Owns a native drag sequence on its creating STA.</summary>
public interface IWindowsShellDropSession : IDisposable
{
	/// <summary>Tries to forward the initial drag entry to the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <param name="acceptedEffect">The operation currently accepted by the Shell target.</param>
	/// <returns><see langword="true"/> when the Shell target accepted the drag sequence, even if it does not currently accept an operation.</returns>
	bool TryDragEnter(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects, out WindowsShellDropEffects acceptedEffect);

	/// <summary>Forwards pointer movement within the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <returns>The operation accepted by the Shell target.</returns>
	WindowsShellDropEffects DragOver(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects);

	/// <summary>Notifies the Shell target that the pointer left it.</summary>
	void DragLeave();

	/// <summary>Forwards the completed drop to the Shell target.</summary>
	/// <param name="modifiers">The active pointer buttons and modifier keys.</param>
	/// <param name="screenPoint">The pointer position in physical screen coordinates.</param>
	/// <param name="allowedEffects">The operations allowed by the drag source.</param>
	/// <returns>The operation performed by the Shell target.</returns>
	WindowsShellDropEffects Drop(WindowsShellDragDropModifiers modifiers, Point screenPoint, WindowsShellDropEffects allowedEffects);
}

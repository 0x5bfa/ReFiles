// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Windows.ApplicationModel.DataTransfer;

namespace Files.Core.Storage.Windows;

/// <summary>Attaches an apartment-neutral Shell selection on the calling STA.</summary>
public interface IWindowsShellDragSource
{
	/// <summary>Attaches the Shell selection to a data package on the calling STA.</summary>
	/// <param name="dataPackage">The destination WinRT data package.</param>
	/// <param name="ownerWindowHandle">The borrowed owner window handle.</param>
	/// <param name="preferredEffect">The preferred transfer operation.</param>
	/// <param name="deriveMoveFromDelete">Whether delete capability should advertise move support.</param>
	/// <returns>The operations supported by the selection.</returns>
	WindowsShellDropEffects Attach(DataPackage dataPackage, nint ownerWindowHandle, WindowsShellDropEffects preferredEffect = WindowsShellDropEffects.None, bool deriveMoveFromDelete = true);
}

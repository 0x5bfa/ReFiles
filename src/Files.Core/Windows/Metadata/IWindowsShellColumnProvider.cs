// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Provides column metadata for a Windows Shell browse location.</summary>
public interface IWindowsShellColumnProvider
{
	/// <summary>Gets the columns exposed by the current Windows Shell location.</summary>
	/// <param name="cancellationToken">The token used to cancel the Shell operation.</param>
	/// <returns>The Shell column metadata, or <see langword="null"/> when it is unavailable.</returns>
	ValueTask<WindowsShellColumnSet?> GetColumnsAsync(CancellationToken cancellationToken = default);
}

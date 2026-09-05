// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Creates Windows Shell preview sessions.</summary>
public interface IWindowsShellPreviewSessionFactory
{
	/// <summary>Creates a session for a resolved preview handler and host.</summary>
	/// <param name="result">The resolved preview handler.</param>
	/// <param name="host">The preview host window.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The created preview session.</returns>
	ValueTask<IWindowsShellPreviewSession> CreateAsync(WindowsShellPreviewResult result, WindowsPreviewHost host, CancellationToken cancellationToken = default);
}

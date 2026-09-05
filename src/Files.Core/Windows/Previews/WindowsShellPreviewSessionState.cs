// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Describes the lifecycle state of a Windows Shell preview session.</summary>
public enum WindowsShellPreviewSessionState
{
	/// <summary>The session has been created.</summary>
	Created,
	/// <summary>The preview handler is being activated.</summary>
	Activating,
	/// <summary>The preview handler has been initialized.</summary>
	Initialized,
	/// <summary>The preview handler is rendering a preview.</summary>
	Previewing,
	/// <summary>Activation or rendering failed.</summary>
	Faulted,
	/// <summary>The session has been disposed.</summary>
	Disposed,
}

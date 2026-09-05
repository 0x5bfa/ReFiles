// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>Specifies the process context allowed for preview handler activation.</summary>
[Flags]
public enum WindowsPreviewHandlerActivationContext : uint
{
	/// <summary>Activate an in-process preview handler.</summary>
	InProcessServer = 0x1,
	/// <summary>Activate a local-server preview handler.</summary>
	LocalServer = 0x4,
	/// <summary>Use the caller's impersonation token when activating the local server.</summary>
	EnableCloaking = 0x100000,
}

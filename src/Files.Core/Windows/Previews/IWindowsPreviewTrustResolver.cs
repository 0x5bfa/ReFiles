// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Capabilities;

namespace Files.Core.Windows;

internal interface IWindowsPreviewTrustResolver
{
	WindowsPreviewTrustResult GetTrust(ItemContext context);
}

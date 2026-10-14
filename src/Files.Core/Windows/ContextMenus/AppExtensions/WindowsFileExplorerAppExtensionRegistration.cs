// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

internal sealed record WindowsFileExplorerAppExtensionRegistration(Guid ClassId, string VerbId, string DisplayName, string PackageFullName,
	bool IsCloudProviderVerb = false, string? StaticDisplayName = null, string? StaticIconPath = null);

internal sealed record WindowsFileExplorerAppExtensionGroup(string PackageFullName, string DisplayName, string? LogoPath, bool? IsL1Default,
	IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> Registrations);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains native Windows data used to render a selection's property pages without creating native property-sheet windows.
/// </summary>
public sealed class WindowsShellPropertySheetData
{
	/// <summary>Gets the pages that apply to the selection in display order.</summary>
	public IReadOnlyList<WindowsShellPropertyPage> Pages { get; }

	/// <summary>Gets drive capabilities when the selected item is a volume root.</summary>
	public WindowsShellDriveProperties? Drive { get; }

	/// <summary>Gets Shell link data when the selected item is a shortcut.</summary>
	public WindowsShellShortcutProperties? Shortcut { get; }

	/// <summary>Gets application compatibility data when the selection resolves to an executable.</summary>
	public WindowsShellCompatibilityProperties? Compatibility { get; }

	/// <summary>Gets SMB sharing data when the selected item is a folder.</summary>
	public WindowsShellSharingProperties? Sharing { get; }

	/// <summary>Gets NTFS access-control data when the selection contains file-system items.</summary>
	public WindowsShellSecurityProperties? Security { get; }

	/// <summary>Gets the previous versions discovered for the selected item.</summary>
	public IReadOnlyList<WindowsShellPreviousVersion> PreviousVersions { get; }

	/// <summary>Gets the storage devices displayed by the drive Hardware page.</summary>
	public IReadOnlyList<WindowsShellHardwareDevice> HardwareDevices { get; }

	/// <summary>Gets NTFS disk-quota state when the selected volume supports quotas.</summary>
	public WindowsShellQuotaProperties? Quota { get; }

	/// <summary>Gets folder customization data when the selected item is a folder.</summary>
	public WindowsShellFolderCustomizationProperties? Customization { get; }

	/// <summary>Gets embedded Authenticode signatures.</summary>
	public IReadOnlyList<WindowsShellDigitalSignature> EmbeddedSignatures { get; }

	/// <summary>Gets signatures supplied by catalogs that contain the selected file.</summary>
	public IReadOnlyList<WindowsShellDigitalSignature> CatalogSignatures { get; }

	/// <summary>Gets ordered values from the Shell full-details property list.</summary>
	public IReadOnlyList<WindowsShellPropertyValue> Details { get; }

	internal WindowsShellPropertySheetData(
		IReadOnlyList<WindowsShellPropertyPage> pages,
		WindowsShellDriveProperties? drive,
		WindowsShellShortcutProperties? shortcut,
		WindowsShellCompatibilityProperties? compatibility,
		WindowsShellSharingProperties? sharing,
		WindowsShellSecurityProperties? security,
		IReadOnlyList<WindowsShellPreviousVersion> previousVersions,
		IReadOnlyList<WindowsShellHardwareDevice> hardwareDevices,
		WindowsShellQuotaProperties? quota,
		WindowsShellFolderCustomizationProperties? customization,
		IReadOnlyList<WindowsShellDigitalSignature> embeddedSignatures,
		IReadOnlyList<WindowsShellDigitalSignature> catalogSignatures,
		IReadOnlyList<WindowsShellPropertyValue> details)
	{
		Pages = pages;
		Drive = drive;
		Shortcut = shortcut;
		Compatibility = compatibility;
		Sharing = sharing;
		Security = security;
		PreviousVersions = previousVersions;
		HardwareDevices = hardwareDevices;
		Quota = quota;
		Customization = customization;
		EmbeddedSignatures = embeddedSignatures;
		CatalogSignatures = catalogSignatures;
		Details = details;
	}
}

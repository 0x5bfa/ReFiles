// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Identifies a property page implemented by ReFiles from Windows Shell data sources.
/// </summary>
public enum WindowsShellPropertyPageKind
{
	/// <summary>The common file-system information page.</summary>
	General,

	/// <summary>The drive error-checking and optimization page.</summary>
	Tools,

	/// <summary>The storage-device inventory page.</summary>
	Hardware,

	/// <summary>The Shell link configuration page.</summary>
	Shortcut,

	/// <summary>The application compatibility page.</summary>
	Compatibility,

	/// <summary>The SMB sharing page.</summary>
	Sharing,

	/// <summary>The NTFS discretionary access control page.</summary>
	Security,

	/// <summary>The File History and volume snapshot page.</summary>
	PreviousVersions,

	/// <summary>The NTFS disk-quota page.</summary>
	Quota,

	/// <summary>The folder customization page.</summary>
	Customize,

	/// <summary>The Authenticode and catalog signature page.</summary>
	DigitalSignatures,

	/// <summary>The Windows property-system details page.</summary>
	Details,
}

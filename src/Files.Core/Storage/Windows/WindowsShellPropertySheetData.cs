// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Identifies a property page implemented by ReFiles from Windows Shell data sources.
/// </summary>
public enum WindowsShellPropertyPageKind
{
	/// <summary>The common file-system information page.</summary>
	General,

	/// <summary>The Shell link configuration page.</summary>
	Shortcut,

	/// <summary>The SMB sharing page.</summary>
	Sharing,

	/// <summary>The NTFS discretionary access control page.</summary>
	Security,

	/// <summary>The File History and volume snapshot page.</summary>
	PreviousVersions,

	/// <summary>The folder customization page.</summary>
	Customize,

	/// <summary>The Authenticode and catalog signature page.</summary>
	DigitalSignatures,

	/// <summary>The Windows property-system details page.</summary>
	Details,
}

/// <summary>
/// Contains native Windows data used to render a selection's property pages without creating native property-sheet windows.
/// </summary>
public sealed class WindowsShellPropertySheetData
{
	/// <summary>Gets the pages that apply to the selection in display order.</summary>
	public IReadOnlyList<WindowsShellPropertyPage> Pages { get; }

	/// <summary>Gets Shell link data when the selected item is a shortcut.</summary>
	public WindowsShellShortcutProperties? Shortcut { get; }

	/// <summary>Gets SMB sharing data when the selected item is a folder.</summary>
	public WindowsShellSharingProperties? Sharing { get; }

	/// <summary>Gets NTFS access-control data when the selection contains file-system items.</summary>
	public WindowsShellSecurityProperties? Security { get; }

	/// <summary>Gets the previous versions discovered for the selected item.</summary>
	public IReadOnlyList<WindowsShellPreviousVersion> PreviousVersions { get; }

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
		WindowsShellShortcutProperties? shortcut,
		WindowsShellSharingProperties? sharing,
		WindowsShellSecurityProperties? security,
		IReadOnlyList<WindowsShellPreviousVersion> previousVersions,
		WindowsShellFolderCustomizationProperties? customization,
		IReadOnlyList<WindowsShellDigitalSignature> embeddedSignatures,
		IReadOnlyList<WindowsShellDigitalSignature> catalogSignatures,
		IReadOnlyList<WindowsShellPropertyValue> details)
	{
		Pages = pages;
		Shortcut = shortcut;
		Sharing = sharing;
		Security = security;
		PreviousVersions = previousVersions;
		Customization = customization;
		EmbeddedSignatures = embeddedSignatures;
		CatalogSignatures = catalogSignatures;
		Details = details;
	}
}

/// <summary>
/// Contains values read from an <c>IShellLinkW</c> object.
/// </summary>
public sealed class WindowsShellShortcutProperties
{
	/// <summary>Gets the shortcut target path.</summary>
	public string TargetPath { get; }

	/// <summary>Gets the target's Shell type description.</summary>
	public string TargetType { get; }

	/// <summary>Gets the target's containing folder name.</summary>
	public string TargetLocation { get; }

	/// <summary>Gets the command-line arguments.</summary>
	public string Arguments { get; }

	/// <summary>Gets the working directory.</summary>
	public string WorkingDirectory { get; }

	/// <summary>Gets the encoded Shell hotkey.</summary>
	public ushort Hotkey { get; }

	/// <summary>Gets the target window show command.</summary>
	public int ShowCommand { get; }

	/// <summary>Gets the shortcut comment.</summary>
	public string Comment { get; }

	/// <summary>Gets the custom icon path.</summary>
	public string IconPath { get; }

	/// <summary>Gets the custom icon resource index.</summary>
	public int IconIndex { get; }

	internal WindowsShellShortcutProperties(
		string targetPath,
		string targetType,
		string targetLocation,
		string arguments,
		string workingDirectory,
		ushort hotkey,
		int showCommand,
		string comment,
		string iconPath,
		int iconIndex)
	{
		TargetPath = targetPath;
		TargetType = targetType;
		TargetLocation = targetLocation;
		Arguments = arguments;
		WorkingDirectory = workingDirectory;
		Hotkey = hotkey;
		ShowCommand = showCommand;
		Comment = comment;
		IconPath = iconPath;
		IconIndex = iconIndex;
	}
}

/// <summary>
/// Contains SMB sharing state for a folder.
/// </summary>
public sealed class WindowsShellSharingProperties
{
	/// <summary>Gets a value indicating whether the folder is inside an SMB share.</summary>
	public bool IsShared { get; }

	/// <summary>Gets the containing share name.</summary>
	public string ShareName { get; }

	/// <summary>Gets the UNC path that addresses the selected folder.</summary>
	public string NetworkPath { get; }

	internal WindowsShellSharingProperties(bool isShared, string shareName, string networkPath)
	{
		IsShared = isShared;
		ShareName = shareName;
		NetworkPath = networkPath;
	}
}

/// <summary>
/// Contains NTFS discretionary access-control state for one file-system item.
/// </summary>
public sealed class WindowsShellSecurityProperties
{
	/// <summary>Gets the path whose access-control list was read.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the principals present in the discretionary access-control list.</summary>
	public IReadOnlyList<WindowsShellSecurityPrincipal> Principals { get; }

	internal WindowsShellSecurityProperties(string objectPath, IReadOnlyList<WindowsShellSecurityPrincipal> principals)
	{
		ObjectPath = objectPath;
		Principals = principals;
	}
}

/// <summary>
/// Describes the access masks assigned to a security principal.
/// </summary>
public sealed class WindowsShellSecurityPrincipal
{
	/// <summary>Gets the account's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the account's string SID.</summary>
	public string Sid { get; }

	/// <summary>Gets the combined allowed access mask.</summary>
	public uint AllowedAccessMask { get; }

	/// <summary>Gets the combined denied access mask.</summary>
	public uint DeniedAccessMask { get; }

	internal WindowsShellSecurityPrincipal(string name, string sid, uint allowedAccessMask, uint deniedAccessMask)
	{
		Name = name;
		Sid = sid;
		AllowedAccessMask = allowedAccessMask;
		DeniedAccessMask = deniedAccessMask;
	}
}

/// <summary>
/// Describes one File History or volume snapshot version.
/// </summary>
public sealed class WindowsShellPreviousVersion
{
	/// <summary>Gets the version's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the version's source path.</summary>
	public string SourcePath { get; }

	/// <summary>Gets the version timestamp.</summary>
	public DateTimeOffset DateModified { get; }

	internal WindowsShellPreviousVersion(string name, string sourcePath, DateTimeOffset dateModified)
	{
		Name = name;
		SourcePath = sourcePath;
		DateModified = dateModified;
	}
}

/// <summary>
/// Contains folder customization values read through the Shell customization API.
/// </summary>
public sealed class WindowsShellFolderCustomizationProperties
{
	/// <summary>Gets the folder-kind canonical name.</summary>
	public string FolderKind { get; }

	/// <summary>Gets the folder picture path.</summary>
	public string PicturePath { get; }

	/// <summary>Gets the custom folder icon path.</summary>
	public string IconPath { get; }

	/// <summary>Gets the custom folder icon resource index.</summary>
	public int IconIndex { get; }

	internal WindowsShellFolderCustomizationProperties(string folderKind, string picturePath, string iconPath, int iconIndex)
	{
		FolderKind = folderKind;
		PicturePath = picturePath;
		IconPath = iconPath;
		IconIndex = iconIndex;
	}
}

/// <summary>
/// Describes an embedded or catalog Authenticode signature.
/// </summary>
public sealed class WindowsShellDigitalSignature
{
	/// <summary>Gets the signer certificate subject.</summary>
	public string Signer { get; }

	/// <summary>Gets the message digest algorithm.</summary>
	public string DigestAlgorithm { get; }

	/// <summary>Gets the signature timestamp text when available.</summary>
	public string Timestamp { get; }

	/// <summary>Gets the containing catalog path for a catalog signature.</summary>
	public string CatalogPath { get; }

	internal WindowsShellDigitalSignature(string signer, string digestAlgorithm, string timestamp, string catalogPath)
	{
		Signer = signer;
		DigestAlgorithm = digestAlgorithm;
		Timestamp = timestamp;
		CatalogPath = catalogPath;
	}
}

/// <summary>
/// Contains one formatted value from a Shell property-description list.
/// </summary>
public sealed class WindowsShellPropertyValue
{
	/// <summary>Gets the localized property display name.</summary>
	public string Name { get; }

	/// <summary>Gets the property value formatted by its property description.</summary>
	public string Value { get; }

	/// <summary>Gets a value indicating whether this entry is a property group heading.</summary>
	public bool IsGroup { get; }

	internal WindowsShellPropertyValue(string name, string value, bool isGroup)
	{
		Name = name;
		Value = value;
		IsGroup = isGroup;
	}
}

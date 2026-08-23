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

/// <summary>
/// Contains the executable resolved for Explorer's application compatibility page.
/// </summary>
public sealed class WindowsShellCompatibilityProperties
{
	/// <summary>Gets the executable path whose compatibility settings are managed.</summary>
	public string ExecutablePath { get; }

	internal WindowsShellCompatibilityProperties(string executablePath)
	{
		ExecutablePath = executablePath;
	}
}

/// <summary>
/// Contains capabilities used by the drive General and Tools pages.
/// </summary>
public sealed class WindowsShellDriveProperties
{
	/// <summary>Gets the normalized volume-root path.</summary>
	public string RootPath { get; }

	/// <summary>Gets the value returned by <c>GetDriveTypeW</c>.</summary>
	public uint DriveType { get; }

	/// <summary>Gets the filesystem capability flags returned by <c>GetVolumeInformationW</c>.</summary>
	public uint FileSystemFlags { get; }

	/// <summary>Gets a value indicating whether Explorer exposes its error-checking command.</summary>
	public bool SupportsErrorChecking { get; }

	/// <summary>Gets a value indicating whether Explorer exposes its optimization command.</summary>
	public bool SupportsOptimization { get; }

	internal WindowsShellDriveProperties(string rootPath, uint driveType, uint fileSystemFlags, bool supportsErrorChecking, bool supportsOptimization)
	{
		RootPath = rootPath;
		DriveType = driveType;
		FileSystemFlags = fileSystemFlags;
		SupportsErrorChecking = supportsErrorChecking;
		SupportsOptimization = supportsOptimization;
	}
}

/// <summary>
/// Describes one device displayed by Explorer's drive Hardware page.
/// </summary>
public sealed class WindowsShellHardwareDevice
{
	/// <summary>Gets the PNG data for the device icon loaded by SetupAPI.</summary>
	public ReadOnlyMemory<byte> IconData { get; }

	/// <summary>Gets the device's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the localized setup-class description.</summary>
	public string Type { get; }

	/// <summary>Gets the device manufacturer.</summary>
	public string Manufacturer { get; }

	/// <summary>Gets the device location description.</summary>
	public string Location { get; }

	/// <summary>Gets the device UI location number when one is assigned.</summary>
	public uint? LocationNumber { get; }

	/// <summary>Gets the provider-supplied format for the device UI location number.</summary>
	public string LocationNumberFormat { get; }

	/// <summary>Gets the Configuration Manager status flags.</summary>
	public uint Status { get; }

	/// <summary>Gets the Configuration Manager problem code.</summary>
	public uint ProblemCode { get; }

	/// <summary>Gets the stable device-instance identifier.</summary>
	public string InstanceId { get; }

	internal WindowsShellHardwareDevice(
		ReadOnlyMemory<byte> iconData,
		string name,
		string type,
		string manufacturer,
		string location,
		uint? locationNumber,
		string locationNumberFormat,
		uint status,
		uint problemCode,
		string instanceId)
	{
		IconData = iconData;
		Name = name;
		Type = type;
		Manufacturer = manufacturer;
		Location = location;
		LocationNumber = locationNumber;
		LocationNumberFormat = locationNumberFormat;
		Status = status;
		ProblemCode = problemCode;
		InstanceId = instanceId;
	}
}

/// <summary>
/// Contains the default NTFS quota policy for a volume.
/// </summary>
public sealed class WindowsShellQuotaProperties
{
	/// <summary>Gets the volume root path.</summary>
	public string RootPath { get; }

	/// <summary>Gets the Shell display name for the volume.</summary>
	public string DisplayName { get; }

	/// <summary>Gets a value indicating whether reading quota policy requires elevation.</summary>
	public bool RequiresElevation { get; }

	/// <summary>Gets a value indicating whether quota tracking is enabled.</summary>
	public bool IsTrackingEnabled { get; }

	/// <summary>Gets a value indicating whether quota limits are enforced.</summary>
	public bool IsLimitEnforced { get; }

	/// <summary>Gets a value indicating whether limit events are logged.</summary>
	public bool LogsLimitEvents { get; }

	/// <summary>Gets a value indicating whether warning events are logged.</summary>
	public bool LogsWarningEvents { get; }

	/// <summary>Gets the default per-user quota limit in bytes, or -1 for no limit.</summary>
	public long DefaultLimit { get; }

	/// <summary>Gets the default per-user warning threshold in bytes, or -1 for no threshold.</summary>
	public long DefaultThreshold { get; }

	internal WindowsShellQuotaProperties(
		string rootPath,
		string displayName,
		bool requiresElevation,
		bool isTrackingEnabled,
		bool isLimitEnforced,
		bool logsLimitEvents,
		bool logsWarningEvents,
		long defaultLimit,
		long defaultThreshold)
	{
		RootPath = rootPath;
		DisplayName = displayName;
		RequiresElevation = requiresElevation;
		IsTrackingEnabled = isTrackingEnabled;
		IsLimitEnforced = isLimitEnforced;
		LogsLimitEvents = logsLimitEvents;
		LogsWarningEvents = logsWarningEvents;
		DefaultLimit = defaultLimit;
		DefaultThreshold = defaultThreshold;
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
	/// <summary>Gets the local folder path represented by the page.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the folder name displayed by the page.</summary>
	public string DisplayName { get; }

	/// <summary>Gets a value indicating whether the folder is inside an SMB share.</summary>
	public bool IsShared { get; }

	/// <summary>Gets the containing share name.</summary>
	public string ShareName { get; }

	/// <summary>Gets the UNC path that addresses the selected folder.</summary>
	public string NetworkPath { get; }

	/// <summary>Gets a value indicating whether password-protection guidance is applicable to this computer.</summary>
	public bool ShowPasswordProtection { get; }

	/// <summary>Gets a value indicating whether users must authenticate to access shared folders.</summary>
	public bool IsPasswordProtectionEnabled { get; }

	internal WindowsShellSharingProperties(string objectPath, string displayName, bool isShared, string shareName, string networkPath, bool showPasswordProtection, bool isPasswordProtectionEnabled)
	{
		ObjectPath = objectPath;
		DisplayName = displayName;
		IsShared = isShared;
		ShareName = shareName;
		NetworkPath = networkPath;
		ShowPasswordProtection = showPasswordProtection;
		IsPasswordProtectionEnabled = isPasswordProtectionEnabled;
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

	/// <summary>Gets the PNG data for the ACLUI principal icon.</summary>
	public ReadOnlyMemory<byte> IconData { get; }

	/// <summary>Gets the image index retained for compatibility with the ACLUI image-list source.</summary>
	public int IconIndex { get; }

	/// <summary>Gets the combined allowed access mask.</summary>
	public uint AllowedAccessMask { get; }

	/// <summary>Gets the combined denied access mask.</summary>
	public uint DeniedAccessMask { get; }

	internal WindowsShellSecurityPrincipal(string name, string sid, ReadOnlyMemory<byte> iconData, int iconIndex, uint allowedAccessMask, uint deniedAccessMask)
	{
		Name = name;
		Sid = sid;
		IconData = iconData;
		IconIndex = iconIndex;
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
	/// <summary>Gets the customized folder path.</summary>
	public string ObjectPath { get; }

	/// <summary>Gets the folder-kind canonical name.</summary>
	public string FolderKind { get; }

	/// <summary>Gets the folder picture path.</summary>
	public string PicturePath { get; }

	/// <summary>Gets the custom folder icon path.</summary>
	public string IconPath { get; }

	/// <summary>Gets the custom folder icon resource index.</summary>
	public int IconIndex { get; }

	/// <summary>Gets a value indicating whether Explorer exposes folder-picture customization for this folder.</summary>
	public bool CanChangePicture { get; }

	/// <summary>Gets a value indicating whether Explorer exposes folder-icon customization for this folder.</summary>
	public bool CanChangeIcon { get; }

	/// <summary>Gets a value indicating whether the current template is also stored in Explorer's inherited view-state bag.</summary>
	public bool ApplyToSubfolders { get; }

	internal WindowsShellFolderCustomizationProperties(string objectPath, string folderKind, string picturePath, string iconPath, int iconIndex, bool canChangePicture, bool canChangeIcon,
		bool applyToSubfolders)
	{
		ObjectPath = objectPath;
		FolderKind = folderKind;
		PicturePath = picturePath;
		IconPath = iconPath;
		IconIndex = iconIndex;
		CanChangePicture = canChangePicture;
		CanChangeIcon = canChangeIcon;
		ApplyToSubfolders = applyToSubfolders;
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

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authorization;
using Windows.Win32.Security.Cryptography;
using Windows.Win32.Security.Cryptography.Catalog;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.IO;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsShellPropertySheetReader
{
	private const byte AccessAllowedAceType = 0;
	private const byte AccessDeniedAceType = 1;
	private const uint ErrorMoreData = 234;
	private const uint MaximumPreferredLength = uint.MaxValue;
	private const int ShellStringCapacity = 32_768;
	private const uint ShellLinkRawPath = 4;
	private const uint CryptographicMessageSignerCount = 5;
	private const uint CryptographicMessageSignerInfo = 6;
	private const uint CertificateNameSimpleDisplayType = 4;
	private const uint DiskQuotaStateTrack = 1;
	private const uint DiskQuotaStateEnforce = 2;
	private const uint DiskQuotaLogUserThreshold = 1;
	private const uint DiskQuotaLogUserLimit = 2;
	private const int AccessDeniedResult = unchecked((int)0x80070005);
	private const uint SnapshotControlCode = 0x00144064;
	private const int SnapshotHeaderSize = 12;
	private const int StatusPending = 0x00000103;
	private const string SnapshotNameFormat = "'@GMT-'yyyy.MM.dd-HH.mm.ss";

	internal static WindowsShellPropertySheetData CreateEmpty(IReadOnlyList<WindowsShellPropertyPage> pages)
	{
		return new(pages, null, null, null, null, null, [], [], null, null, [], [], []);
	}

	internal static WindowsShellPropertySheetData Read(IShellItem primaryItem, WindowsShellResolvedSelection selection, IReadOnlyList<WindowsShellPropertyPage> pages, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(primaryItem);
		ArgumentNullException.ThrowIfNull(selection);
		ArgumentNullException.ThrowIfNull(pages);

		cancellationToken.ThrowIfCancellationRequested();
		var primaryPath = selection.FileSystemPaths.Count is 1 ? selection.FileSystemPaths[0] : null;
		var drive = primaryPath is not null && WindowsShellPropertyPageEnumerator.TryGetDriveRoot(primaryPath, out var driveRoot) ? ReadDriveProperties(driveRoot, pages) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var shortcut = primaryPath is null ? null : TryReadShortcut(primaryPath);
		cancellationToken.ThrowIfCancellationRequested();
		var readsCompatibility = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Compatibility);
		var compatibility = readsCompatibility && primaryPath is not null ? TryReadCompatibility(primaryPath, shortcut) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsSharing = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Sharing);
		var sharing = readsSharing && primaryPath is not null ? ReadSharing(primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsSecurity = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Security);
		var security = readsSecurity && primaryPath is not null ? TryReadSecurity(primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsPreviousVersions = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.PreviousVersions);
		var previousVersions = readsPreviousVersions && primaryPath is not null ? ReadPreviousVersions(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var readsHardware = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Hardware);
		var hardwareDevices = readsHardware ? ReadHardwareDevices(cancellationToken) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var readsQuota = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Quota);
		var quota = readsQuota && drive is not null ? TryReadQuota(drive.RootPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsCustomization = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Customize);
		var customization = readsCustomization && primaryPath is not null ? TryReadFolderCustomization(primaryItem, primaryPath) : null;
		cancellationToken.ThrowIfCancellationRequested();
		var readsSignatures = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.DigitalSignatures);
		var embeddedSignatures = readsSignatures && primaryPath is not null && File.Exists(primaryPath) ? ReadEmbeddedSignatures(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var catalogSignatures = readsSignatures && primaryPath is not null && File.Exists(primaryPath) ? ReadCatalogSignatures(primaryPath) : [];
		cancellationToken.ThrowIfCancellationRequested();
		var readsDetails = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Details);
		var details = readsDetails ? ReadDetails(primaryItem) : [];

		return new(pages, drive, shortcut, compatibility, sharing, security, previousVersions, hardwareDevices, quota, customization, embeddedSignatures, catalogSignatures, details);
	}

	internal static string? TryResolveShortcutTarget(string path)
	{
		if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		try
		{
			var link = ShellLink.CreateInstance<IShellLinkW>();
			if (link is not IPersistFile persistedLink || persistedLink.Load(path, STGM.STGM_READ).Failed)
			{
				return null;
			}

			Span<char> targetBuffer = stackalloc char[ShellStringCapacity];
			var findData = new WIN32_FIND_DATAW();
			link.GetPath(targetBuffer, ref findData, ShellLinkRawPath);

			return ReadNullTerminated(targetBuffer);
		}
		catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static WindowsShellDriveProperties ReadDriveProperties(string root, IReadOnlyList<WindowsShellPropertyPage> pages)
	{
		PInvoke.GetVolumeInformation(root, [], out _, out _, out var fileSystemFlags, []);
		var hasTools = pages.Any(static page => page.Kind is WindowsShellPropertyPageKind.Tools);

		return new(root, PInvoke.GetDriveType(root), fileSystemFlags, hasTools, hasTools);
	}

	private static IReadOnlyList<WindowsShellHardwareDevice> ReadHardwareDevices(CancellationToken cancellationToken)
	{
		var devices = new List<WindowsShellHardwareDevice>();
		var instanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ReadHardwareClass(PInvoke.GUID_DEVCLASS_DISKDRIVE, devices, instanceIds, cancellationToken);
		ReadHardwareClass(PInvoke.GUID_DEVCLASS_FLOPPYDISK, devices, instanceIds, cancellationToken);
		ReadHardwareClass(PInvoke.GUID_DEVCLASS_CDROM, devices, instanceIds, cancellationToken);
		ReadHardwareClass(PInvoke.GUID_DEVCLASS_SCMDISK, devices, instanceIds, cancellationToken);

		return devices.OrderBy(static device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	private static void ReadHardwareClass(Guid classId, List<WindowsShellHardwareDevice> devices, HashSet<string> instanceIds, CancellationToken cancellationToken)
	{
		using var deviceInfoSet = PInvoke.SetupDiGetClassDevs(classId, null!, default, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PROFILE);
		if (deviceInfoSet.IsInvalid)
		{
			return;
		}

		var classDescriptionBuffer = new char[256];
		for (var index = 0u; ; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var deviceInfo = new SP_DEVINFO_DATA { cbSize = checked((uint)sizeof(SP_DEVINFO_DATA)) };
			if (!PInvoke.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
			{
				break;
			}

			var instanceId = ReadDeviceInstanceId(deviceInfoSet, deviceInfo);
			if (string.IsNullOrEmpty(instanceId) || !instanceIds.Add(instanceId))
			{
				continue;
			}

			var name = ReadDeviceProperty(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_NAME);
			var manufacturer = ReadDeviceProperty(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_Device_Manufacturer);
			var location = ReadDeviceProperty(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_Device_LocationInfo);
			var locationNumber = ReadDeviceUInt32Property(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_Device_UINumber);
			var locationNumberFormat = ReadDeviceProperty(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_Device_UINumberDescFormat);
			if (string.IsNullOrEmpty(location))
			{
				location = ReadDeviceProperty(deviceInfoSet, deviceInfo, PInvoke.DEVPKEY_Device_LocationPaths);
			}

			classDescriptionBuffer.AsSpan().Clear();
			var type = PInvoke.SetupDiGetClassDescription(deviceInfo.ClassGuid, classDescriptionBuffer) ? ReadNullTerminated(classDescriptionBuffer) : string.Empty;
			var configurationResult = PInvoke.CM_Get_DevNode_Status(out var status, out var problemCode, deviceInfo.DevInst, 0);
			if (configurationResult is not CONFIGRET.CR_SUCCESS)
			{
				status = 0;
				problemCode = 0;
			}

			ReadOnlyMemory<byte> iconData = ReadOnlyMemory<byte>.Empty;
			if (PInvoke.SetupDiLoadDeviceIcon(deviceInfoSet, deviceInfo, 32, 32, 0, out var icon))
			{
				using (icon)
				{
					iconData = WindowsThumbnailRenderer.EncodeHIcon(icon, 32, cancellationToken) ?? [];
				}
			}

			if (iconData.IsEmpty)
			{
				var stockIcon = classId == PInvoke.GUID_DEVCLASS_CDROM ? SHSTOCKICONID.SIID_DRIVECD : classId == PInvoke.GUID_DEVCLASS_FLOPPYDISK ? SHSTOCKICONID.SIID_DRIVEREMOVE : SHSTOCKICONID.SIID_DRIVEFIXED;
				iconData = WindowsShellIconProvider.GetStockIcon(stockIcon, 32, cancellationToken);
			}

			devices.Add(new(iconData, string.IsNullOrEmpty(name) ? instanceId : name, type, manufacturer, location, locationNumber, locationNumberFormat, (uint)status, (uint)problemCode, instanceId));
		}
	}

	private static string ReadDeviceInstanceId(SafeHandle deviceInfoSet, SP_DEVINFO_DATA deviceInfo)
	{
		Span<char> buffer = stackalloc char[512];

		return PInvoke.SetupDiGetDeviceInstanceId(deviceInfoSet, deviceInfo, buffer) ? ReadNullTerminated(buffer) : string.Empty;
	}

	private static string ReadDeviceProperty(SafeHandle deviceInfoSet, SP_DEVINFO_DATA deviceInfo, DEVPROPKEY propertyKey)
	{
		PInvoke.SetupDiGetDeviceProperty(deviceInfoSet, deviceInfo, propertyKey, out _, [], out var requiredSize, 0);
		if (requiredSize < sizeof(char) || requiredSize > ShellStringCapacity * sizeof(char))
		{
			return string.Empty;
		}

		var buffer = new byte[requiredSize];
		if (!PInvoke.SetupDiGetDeviceProperty(deviceInfoSet, deviceInfo, propertyKey, out _, buffer, 0))
		{
			return string.Empty;
		}

		var values = MemoryMarshal.Cast<byte, char>(buffer);
		var value = ReadNullTerminated(values);

		return value;
	}

	private static uint? ReadDeviceUInt32Property(SafeHandle deviceInfoSet, SP_DEVINFO_DATA deviceInfo, DEVPROPKEY propertyKey)
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		var succeeded = PInvoke.SetupDiGetDeviceProperty(deviceInfoSet, deviceInfo, propertyKey, out var propertyType, buffer, out var requiredSize, 0);
		if (!succeeded || propertyType is not DEVPROPTYPE.DEVPROP_TYPE_UINT32 || requiredSize != sizeof(uint))
		{
			return null;
		}

		return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
	}

	private static WindowsShellQuotaProperties? TryReadQuota(string root)
	{
		var displayName = ReadVolumeDisplayName(root);
		try
		{
			var createResult = PInvoke.CoCreateInstance(CLSID.CLSID_DiskQuotaControl, null, CLSCTX.CLSCTX_INPROC_SERVER, out IDiskQuotaControl? quotaControl);
			if (createResult.Failed || quotaControl is null)
			{
				return null;
			}

			fixed (char* rootPointer = root)
			{
				var initializeResult = quotaControl.Initialize(new PCWSTR(rootPointer), false);
				if (initializeResult.Value is AccessDeniedResult)
				{
					return new(root, displayName, true, false, false, false, false, -1, -1);
				}

				if (initializeResult.Failed)
				{
					return null;
				}
			}

			uint state = 0;
			uint logFlags = 0;
			long defaultLimit = -1;
			long defaultThreshold = -1;
			quotaControl.GetQuotaState(ref state);
			quotaControl.GetQuotaLogFlags(ref logFlags);
			quotaControl.GetDefaultQuotaLimit(ref defaultLimit);
			quotaControl.GetDefaultQuotaThreshold(ref defaultThreshold);

			return new(
				root,
				displayName,
				false,
				(state & DiskQuotaStateTrack) is not 0,
				(state & DiskQuotaStateEnforce) is not 0,
				(logFlags & DiskQuotaLogUserLimit) is not 0,
				(logFlags & DiskQuotaLogUserThreshold) is not 0,
				defaultLimit,
				defaultThreshold);
		}
		catch (COMException)
		{
			return null;
		}
	}

	private static string ReadVolumeDisplayName(string root)
	{
		if (PInvoke.SHCreateItemFromParsingName(root, null, out IShellItem shellItem).Succeeded)
		{
			var displayName = ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_NORMALDISPLAY);
			if (!string.IsNullOrWhiteSpace(displayName))
			{
				return displayName;
			}
		}

		return root;
	}

	private static WindowsShellShortcutProperties? TryReadShortcut(string path)
	{
		if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		try
		{
			var link = ShellLink.CreateInstance<IShellLinkW>();
			if (link is not IPersistFile persistedLink || persistedLink.Load(path, STGM.STGM_READ).Failed)
			{
				return null;
			}

			Span<char> targetBuffer = stackalloc char[ShellStringCapacity];
			Span<char> argumentsBuffer = stackalloc char[ShellStringCapacity];
			Span<char> workingDirectoryBuffer = stackalloc char[ShellStringCapacity];
			Span<char> commentBuffer = stackalloc char[ShellStringCapacity];
			Span<char> iconBuffer = stackalloc char[ShellStringCapacity];
			var findData = new WIN32_FIND_DATAW();
			link.GetPath(targetBuffer, ref findData, ShellLinkRawPath);
			link.GetArguments(argumentsBuffer);
			link.GetWorkingDirectory(workingDirectoryBuffer);
			link.GetDescription(commentBuffer);
			link.GetHotkey(out var hotkey);
			link.GetShowCmd(out var showCommand);
			link.GetIconLocation(iconBuffer, out var iconIndex);
			var targetPath = ReadNullTerminated(targetBuffer);
			var targetType = ReadShellType(targetPath);
			var targetLocation = string.IsNullOrEmpty(targetPath) ? string.Empty : new FileInfo(targetPath).Directory?.Name ?? string.Empty;

			return new(
				targetPath,
				targetType,
				targetLocation,
				ReadNullTerminated(argumentsBuffer),
				ReadNullTerminated(workingDirectoryBuffer),
				hotkey,
				(int)showCommand,
				ReadNullTerminated(commentBuffer),
				ReadNullTerminated(iconBuffer),
				iconIndex);
		}
		catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static WindowsShellCompatibilityProperties? TryReadCompatibility(string path, WindowsShellShortcutProperties? shortcut)
	{
		var executablePath = Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? shortcut?.TargetPath : path;
		if (executablePath is null || !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executablePath))
		{
			return null;
		}

		return new(executablePath);
	}

	private static string ReadShellType(string path)
	{
		if (string.IsNullOrEmpty(path) || PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem shellItem).Failed)
		{
			return string.Empty;
		}

		return ReadShellString(shellItem, "System.ItemTypeText") ?? string.Empty;
	}

	private static WindowsShellSharingProperties ReadSharing(string path)
	{
		return WindowsShellSharingService.ReadProperties(path);
	}

	private static WindowsShellSecurityProperties? TryReadSecurity(string path)
	{
		ACL* discretionaryAcl = null;
		PSECURITY_DESCRIPTOR securityDescriptor = default;
		fixed (char* pathPointer = path)
		{
			var result = PInvoke.GetNamedSecurityInfo(
				new PCWSTR(pathPointer),
				SE_OBJECT_TYPE.SE_FILE_OBJECT,
				OBJECT_SECURITY_INFORMATION.DACL_SECURITY_INFORMATION,
				null,
				null,
				&discretionaryAcl,
				null,
				&securityDescriptor);
			if (result != WIN32_ERROR.ERROR_SUCCESS || securityDescriptor.IsNull)
			{
				return null;
			}
		}

		try
		{
			if (discretionaryAcl is null || !PInvoke.IsValidAcl(*discretionaryAcl))
			{
				return new(path, []);
			}

			var aclInformation = new ACL_SIZE_INFORMATION();
			if (!PInvoke.GetAclInformation(discretionaryAcl, &aclInformation, checked((uint)sizeof(ACL_SIZE_INFORMATION)), ACL_INFORMATION_CLASS.AclSizeInformation))
			{
				return null;
			}

			var principals = new Dictionary<string, SecurityPrincipalBuilder>(StringComparer.OrdinalIgnoreCase);
			for (var index = 0u; index < aclInformation.AceCount; index++)
			{
				if (!PInvoke.GetAce(*discretionaryAcl, index, out var acePointer) || acePointer is null)
				{
					continue;
				}

				var ace = (ACCESS_ALLOWED_ACE*)acePointer;
				if (ace->Header.AceType is not AccessAllowedAceType and not AccessDeniedAceType)
				{
					continue;
				}

				var sid = new PSID(&ace->SidStart);
				var sidText = ReadSid(sid);
				if (string.IsNullOrEmpty(sidText))
				{
					continue;
				}

				if (!principals.TryGetValue(sidText, out var principal))
				{
					var account = ReadAccount(sid, sidText);
					var icon = WindowsSecurityPrincipalIconProvider.GetIcon(sidText, account.Type);
					principal = new(account.Name, sidText, icon.Data, icon.Index);
					principals.Add(sidText, principal);
				}

				if (ace->Header.AceType is AccessAllowedAceType)
				{
					principal.AllowedAccessMask |= ace->Mask;
				}
				else
				{
					principal.DeniedAccessMask |= ace->Mask;
				}
			}

			return new(path, principals.Values.Select(static principal => principal.Create()).ToArray());
		}
		finally
		{
			PInvoke.LocalFree(new HLOCAL((nint)securityDescriptor.Value));
		}
	}

	private static string ReadSid(PSID sid)
	{
		if (!PInvoke.ConvertSidToStringSid(sid, out var value) || value.Value is null)
		{
			return string.Empty;
		}

		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.LocalFree(new HLOCAL((nint)value.Value));
		}
	}

	private static (string Name, SID_NAME_USE Type) ReadAccount(PSID sid, string fallback)
	{
		uint nameLength = 0;
		uint domainLength = 0;
		PInvoke.LookupAccountSid(null!, sid, [], ref nameLength, [], ref domainLength, out var type);
		if (nameLength is 0)
		{
			return (fallback, SID_NAME_USE.SidTypeUnknown);
		}

		var name = new char[nameLength];
		var domain = new char[domainLength];
		if (!PInvoke.LookupAccountSid(null!, sid, name, ref nameLength, domain, ref domainLength, out type))
		{
			return (fallback, SID_NAME_USE.SidTypeUnknown);
		}

		var accountName = ReadNullTerminated(name);
		var domainName = ReadNullTerminated(domain);

		return (string.IsNullOrEmpty(domainName) ? accountName : $"{domainName}\\{accountName}", type);
	}

	private static WindowsShellFolderCustomizationProperties? TryReadFolderCustomization(IShellItem shellItem, string path)
	{
		Span<char> iconBuffer = stackalloc char[ShellStringCapacity];
		Span<char> pictureBuffer = stackalloc char[ShellStringCapacity];
		fixed (char* iconPointer = iconBuffer)
		fixed (char* picturePointer = pictureBuffer)
		{
			var settings = new SHFOLDERCUSTOMSETTINGS
			{
				dwSize = checked((uint)sizeof(SHFOLDERCUSTOMSETTINGS)),
				dwMask = PInvoke.FCSM_ICONFILE | PInvoke.FCSM_LOGO,
				pszIconFile = new PWSTR(iconPointer),
				cchIconFile = checked((uint)iconBuffer.Length),
				pszLogo = new PWSTR(picturePointer),
				cchLogo = checked((uint)pictureBuffer.Length),
			};
			var hasCustomSettings = PInvoke.SHGetSetFolderCustomSettings(ref settings, path, PInvoke.FCS_READ).Succeeded;
			var folderKind = WindowsShellFolderCustomizationService.ReadFolderKind(path, ReadShellString(shellItem, "System.FolderKind") ?? string.Empty);
			var root = Path.GetPathRoot(path);
			var isUncShareRoot = path.StartsWith(@"\\", StringComparison.Ordinal) && root is not null
				&& Path.TrimEndingDirectorySeparator(path).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
			var applyToSubfolders = WindowsShellFolderCustomizationService.IsFolderKindInherited(path, folderKind);

			return new(path, folderKind, hasCustomSettings ? ReadNullTerminated(pictureBuffer) : string.Empty, hasCustomSettings ? ReadNullTerminated(iconBuffer) : string.Empty,
				hasCustomSettings ? settings.iIconIndex : 0, !isUncShareRoot, Directory.Exists(path), applyToSubfolders);
		}
	}

	private static IReadOnlyList<WindowsShellPreviousVersion> ReadPreviousVersions(string path)
	{
		var snapshotPath = path;
		var snapshotNames = ReadSnapshotNames(snapshotPath);
		if (snapshotNames.Count is 0 && TryGetAdministrativeSharePath(path, out var administrativePath))
		{
			snapshotPath = administrativePath;
			snapshotNames = ReadSnapshotNames(snapshotPath);
		}

		if (snapshotNames.Count is 0)
		{
			return [];
		}

		var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrEmpty(name))
		{
			name = path;
		}

		var versions = new List<WindowsShellPreviousVersion>(snapshotNames.Count);
		foreach (var snapshotName in snapshotNames)
		{
			if (DateTimeOffset.TryParseExact(snapshotName, SnapshotNameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
			{
				var sourcePath = BuildSnapshotItemPath(snapshotPath, snapshotName);
				if (File.Exists(sourcePath) || Directory.Exists(sourcePath))
				{
					versions.Add(new(name, sourcePath, timestamp.ToLocalTime()));
				}
			}
		}

		return versions.OrderByDescending(static version => version.DateModified).ToArray();
	}

	private static IReadOnlyList<string> ReadSnapshotNames(string path)
	{
		using var file = PInvoke.CreateFile(
			path,
			(uint)FILE_ACCESS_RIGHTS.FILE_READ_DATA,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
			null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
			null);
		if (file.IsInvalid)
		{
			return [];
		}

		Span<byte> header = stackalloc byte[16];
		if (IssueSnapshotControl(file, header) is not 0)
		{
			return [];
		}

		var snapshotCount = BinaryPrimitives.ReadUInt32LittleEndian(header);
		var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
		if (snapshotCount is 0 || payloadSize is 0 || payloadSize > int.MaxValue - SnapshotHeaderSize)
		{
			return [];
		}

		var output = new byte[checked((int)payloadSize + SnapshotHeaderSize)];
		if (IssueSnapshotControl(file, output) is not 0)
		{
			return [];
		}

		var returnedCount = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint))), snapshotCount);
		var returnedSize = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(sizeof(uint) * 2)), payloadSize);
		var characters = MemoryMarshal.Cast<byte, char>(output.AsSpan(SnapshotHeaderSize, checked((int)returnedSize)));
		var names = new List<string>(checked((int)returnedCount));
		while (names.Count < returnedCount && !characters.IsEmpty)
		{
			var terminator = characters.IndexOf('\0');
			if (terminator <= 0)
			{
				break;
			}

			names.Add(new(characters[..terminator]));
			characters = characters[(terminator + 1)..];
		}

		return names;
	}

	private static int IssueSnapshotControl(SafeHandle file, Span<byte> output)
	{
		using var completedEvent = PInvoke.CreateEvent(null, true, false, null);
		if (completedEvent.IsInvalid)
		{
			return Marshal.GetLastPInvokeError();
		}

		var ioStatus = new IO_STATUS_BLOCK();
		fixed (byte* outputPointer = output)
		{
			var status = PInvoke.NtFsControlFile(file, completedEvent, 0, 0, &ioStatus, SnapshotControlCode, null, 0, outputPointer, checked((uint)output.Length));
			if (status is StatusPending)
			{
				PInvoke.WaitForSingleObjectEx(completedEvent, uint.MaxValue, false);
				status = ioStatus.Status.Value;
			}

			return status;
		}
	}

	private static bool TryGetAdministrativeSharePath(string path, out string administrativePath)
	{
		administrativePath = string.Empty;
		var fullPath = Path.GetFullPath(path);
		var root = Path.GetPathRoot(fullPath);
		if (root is null || root.Length < 2 || root[1] is not ':')
		{
			return false;
		}

		var relativePath = fullPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		administrativePath = $"\\\\localhost\\{char.ToUpperInvariant(root[0])}$";
		if (!string.IsNullOrEmpty(relativePath))
		{
			administrativePath = Path.Combine(administrativePath, relativePath);
		}

		return true;
	}

	private static string BuildSnapshotItemPath(string sourcePath, string snapshotName)
	{
		var root = Path.GetPathRoot(sourcePath) ?? string.Empty;
		var relativePath = sourcePath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var snapshotRoot = Path.Combine(root, snapshotName);

		return string.IsNullOrEmpty(relativePath) ? snapshotRoot : Path.Combine(snapshotRoot, relativePath);
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadEmbeddedSignatures(string path)
	{
		return ReadCryptographicSignatures(path, CERT_QUERY_CONTENT_TYPE_FLAGS.CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED, string.Empty);
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadCryptographicSignatures(string path, CERT_QUERY_CONTENT_TYPE_FLAGS contentType, string catalogPath)
	{
		HCERTSTORE certificateStore = default;
		CERT_QUERY_ENCODING_TYPE encoding;
		void* message = null;
		fixed (char* pathPointer = path)
		{
			if (!PInvoke.CryptQueryObject(
				CERT_QUERY_OBJECT_TYPE.CERT_QUERY_OBJECT_FILE,
				pathPointer,
				contentType,
				CERT_QUERY_FORMAT_TYPE_FLAGS.CERT_QUERY_FORMAT_FLAG_BINARY,
				0,
				out encoding,
				out _,
				out _,
				out certificateStore,
				out message,
				out _))
			{
				return [];
			}
		}

		try
		{
			uint signerCount = 0;
			var countSize = checked((uint)sizeof(uint));
			if (message is null || !PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerCount, 0, new Span<byte>(&signerCount, sizeof(uint)), ref countSize))
			{
				return [];
			}

			var signatures = new List<WindowsShellDigitalSignature>(checked((int)signerCount));
			for (var index = 0u; index < signerCount; index++)
			{
				var signer = ReadSignerName(certificateStore, encoding, message, index);
				var algorithm = ReadSignerDigestAlgorithm(message, index);
				signatures.Add(new(signer, algorithm, string.Empty, catalogPath));
			}

			return signatures;
		}
		finally
		{
			if (message is not null)
			{
				PInvoke.CryptMsgClose(message);
			}

			if (!certificateStore.IsNull)
			{
				PInvoke.CertCloseStore(certificateStore, 0);
			}
		}
	}

	private static string ReadSignerName(HCERTSTORE certificateStore, CERT_QUERY_ENCODING_TYPE encoding, void* message, uint index)
	{
		uint size = 0;
		PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, [], ref size);
		if (size is 0)
		{
			return string.Empty;
		}

		var buffer = NativeMemory.Alloc(size);
		try
		{
			if (!PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, new Span<byte>(buffer, checked((int)size)), ref size))
			{
				return string.Empty;
			}

			var signerInfo = (CMSG_SIGNER_INFO*)buffer;
			var certificateInfo = new CERT_INFO { Issuer = signerInfo->Issuer, SerialNumber = signerInfo->SerialNumber };
			var certificate = PInvoke.CertFindCertificateInStore(certificateStore, encoding, 0, CERT_FIND_FLAGS.CERT_FIND_SUBJECT_CERT, &certificateInfo, (CERT_CONTEXT*)null);
			if (certificate is null)
			{
				return string.Empty;
			}

			try
			{
				var nameLength = PInvoke.CertGetNameString(in *certificate, CertificateNameSimpleDisplayType, 0, null, []);
				if (nameLength <= 1)
				{
					return string.Empty;
				}

				var name = new char[nameLength];
				PInvoke.CertGetNameString(in *certificate, CertificateNameSimpleDisplayType, 0, null, name);

				return ReadNullTerminated(name);
			}
			finally
			{
				PInvoke.CertFreeCertificateContext(certificate);
			}
		}
		finally
		{
			NativeMemory.Free(buffer);
		}
	}

	private static string ReadSignerDigestAlgorithm(void* message, uint index)
	{
		uint size = 0;
		PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, [], ref size);
		if (size is 0)
		{
			return string.Empty;
		}

		var buffer = NativeMemory.Alloc(size);
		try
		{
			if (!PInvoke.CryptMsgGetParam(message, CryptographicMessageSignerInfo, index, new Span<byte>(buffer, checked((int)size)), ref size))
			{
				return string.Empty;
			}

			var oidValue = ((CMSG_SIGNER_INFO*)buffer)->HashAlgorithm.pszObjId.ToString();
			if (string.IsNullOrEmpty(oidValue))
			{
				return string.Empty;
			}

			return Oid.FromOidValue(oidValue, OidGroup.HashAlgorithm).FriendlyName ?? oidValue;
		}
		catch (CryptographicException)
		{
			return string.Empty;
		}
		finally
		{
			NativeMemory.Free(buffer);
		}
	}

	private static IReadOnlyList<WindowsShellDigitalSignature> ReadCatalogSignatures(string path)
	{
		var catalogPath = FindCatalogPath(path, "SHA256") ?? FindCatalogPath(path, "SHA1");
		if (string.IsNullOrEmpty(catalogPath))
		{
			return [];
		}

		return ReadCryptographicSignatures(catalogPath, CERT_QUERY_CONTENT_TYPE_FLAGS.CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED, catalogPath);
	}

	private static string? FindCatalogPath(string path, string hashAlgorithm)
	{
		if (!PInvoke.CryptCATAdminAcquireContext2(out var catalogAdmin, null, hashAlgorithm, null))
		{
			return null;
		}

		try
		{
			using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
			uint hashSize = 0;
			if (!PInvoke.CryptCATAdminCalcHashFromFileHandle2(catalogAdmin, file, ref hashSize, []) || hashSize is 0)
			{
				return null;
			}

			var hash = new byte[hashSize];
			if (!PInvoke.CryptCATAdminCalcHashFromFileHandle2(catalogAdmin, file, ref hashSize, hash))
			{
				return null;
			}

			var catalogContext = PInvoke.CryptCATAdminEnumCatalogFromHash(catalogAdmin, hash);
			if (catalogContext is 0)
			{
				return null;
			}

			try
			{
				var catalogInformation = new CATALOG_INFO { cbStruct = checked((uint)sizeof(CATALOG_INFO)) };
				if (!PInvoke.CryptCATCatalogInfoFromContext(catalogContext, ref catalogInformation, 0))
				{
					return null;
				}

				return ReadNullTerminated(catalogInformation.wszCatalogFile.AsSpan());
			}
			finally
			{
				PInvoke.CryptCATAdminReleaseCatalogContext(catalogAdmin, catalogContext, 0);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
		finally
		{
			PInvoke.CryptCATAdminReleaseContext(catalogAdmin, 0);
		}
	}

	private static IReadOnlyList<WindowsShellPropertyValue> ReadDetails(IShellItem shellItem)
	{
		if (shellItem is not IShellItem2 shellItem2
			|| shellItem2.GetPropertyDescriptionList<IPropertyDescriptionList>(PInvoke.PKEY_PropList_FullDetails, out var descriptions).Failed
			|| descriptions is null)
		{
			return [];
		}

		if (shellItem2.GetPropertyStore<IPropertyStore>(GETPROPERTYSTOREFLAGS.GPS_BESTEFFORT, out var propertyStore).Failed || propertyStore is null || descriptions.GetCount(out var count).Failed)
		{
			return [];
		}

		var details = new List<WindowsShellPropertyValue>(checked((int)count));
		for (var index = 0u; index < count; index++)
		{
			if (descriptions.GetAt<IPropertyDescription>(index, out var description).Failed || description is null || description.GetPropertyKey(out var key).Failed)
			{
				continue;
			}

			var name = ReadPropertyDescriptionName(description);
			if (string.IsNullOrEmpty(name))
			{
				continue;
			}

			description.GetTypeFlags(PROPDESC_TYPE_FLAGS.PDTF_MASK_ALL, out var typeFlags);
			var isGroup = typeFlags.HasFlag(PROPDESC_TYPE_FLAGS.PDTF_ISGROUP);
			var value = isGroup ? string.Empty : ReadFormattedProperty(propertyStore, description, key);
			details.Add(new(name, value, isGroup));
		}

		return details;
	}

	private static string ReadPropertyDescriptionName(IPropertyDescription description)
	{
		if (description.GetDisplayName(out var displayName).Succeeded && displayName.Value is not null)
		{
			return ReadAndFree(displayName);
		}

		return description.GetCanonicalName(out var canonicalName).Succeeded && canonicalName.Value is not null ? ReadAndFree(canonicalName) : string.Empty;
	}

	private static string ReadFormattedProperty(IPropertyStore propertyStore, IPropertyDescription description, PROPERTYKEY key)
	{
		if (propertyStore.GetValue(key, out PROPVARIANT value).Failed)
		{
			return string.Empty;
		}

		try
		{
			return description.FormatForDisplay(value, PROPDESC_FORMAT_FLAGS.PDFF_DEFAULT, out var formatted).Succeeded && formatted.Value is not null ? ReadAndFree(formatted) : string.Empty;
		}
		finally
		{
			PInvoke.PropVariantClear(ref value);
		}
	}

	private static string? ReadShellString(IShellItem shellItem, string propertyId)
	{
		if (shellItem is not IShellItem2 shellItem2 || PInvoke.PSGetPropertyKeyFromName(propertyId, out var key).Failed || shellItem2.GetString(key, out var value).Failed)
		{
			return null;
		}

		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static string ReadAndFree(PWSTR value)
	{
		try
		{
			return value.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(value.Value);
		}
	}

	private static string ReadNullTerminated(ReadOnlySpan<char> value)
	{
		var terminator = value.IndexOf('\0');

		return new string(value[..(terminator < 0 ? value.Length : terminator)]);
	}

	private sealed class SecurityPrincipalBuilder
	{
		internal string Name { get; }

		internal string Sid { get; }

		internal ReadOnlyMemory<byte> IconData { get; }

		internal int IconIndex { get; }

		internal uint AllowedAccessMask { get; set; }

		internal uint DeniedAccessMask { get; set; }

		internal SecurityPrincipalBuilder(string name, string sid, ReadOnlyMemory<byte> iconData, int iconIndex)
		{
			Name = name;
			Sid = sid;
			IconData = iconData;
			IconIndex = iconIndex;
		}

		internal WindowsShellSecurityPrincipal Create()
		{
			return new(Name, Sid, IconData, IconIndex, AllowedAccessMask, DeniedAccessMask);
		}
	}
}

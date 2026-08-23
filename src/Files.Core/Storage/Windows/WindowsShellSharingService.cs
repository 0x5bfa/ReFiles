// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.NetManagement;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Reads local SMB sharing state and opens the Windows sharing experiences.
/// </summary>
public static unsafe class WindowsShellSharingService
{
	private const uint DriveRemovable = 2;
	private const uint DriveFixed = 3;
	private const uint DriveCdRom = 5;
	private const uint ErrorMoreData = 234;
	private const uint MaximumPreferredLength = uint.MaxValue;
	private const uint OsDomainMember = 28;
	private const string NetworkAndSharingCenterCanonicalName = "Microsoft.NetworkAndSharingCenter";
	private const string NetworkAndSharingCenterPage = "Advanced";
	private const string NetworkSharingLibrary = "ntshrui.dll";
	private const string ShellLightweightUtilityLibrary = "shlwapi.dll";
	private static readonly Guid _multiObjectElevationFactoryClassId = new("36F0BD14-D84D-468C-B79C-9990F3FA897F");
	private static readonly Guid _multiObjectElevationFactoryInterfaceId = new("6FABDA16-031E-47E3-B2A2-2339C05CCB9E");
	private static readonly Guid _openControlPanelClassId = new("06622D85-6856-4460-8DE1-A81921B41C4B");
	private static readonly Guid _openControlPanelInterfaceId = new("D11AD862-66DE-4DF4-BF6C-1F5621996AF1");
	private static readonly Guid _sharingConfigurationManagerClassId = new("49F371E1-8C5C-4D9C-9A3B-54A6827F513C");
	private static readonly Guid _sharingConfigurationManagerInterfaceId = new("14AA4AB8-ABE3-4A07-A290-1D5DCCDD2FC2");
	private static readonly Guid _sharingElevatedFactoryClassId = new("72A7994A-3092-4054-B6BE-08FF81AEEFFC");

	/// <summary>
	/// Opens the Windows sharing wizard for a folder.
	/// </summary>
	/// <param name="owner">The window that owns the wizard.</param>
	/// <param name="path">The local folder path to share.</param>
	/// <returns>The result returned by the Windows sharing provider.</returns>
	public static HRESULT ShowSharingWizard(HWND owner, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (!TryLoadExport(NetworkSharingLibrary, "ShowShareFolderUI", out var module, out var export))
		{
			return HRESULT.E_FAIL;
		}

		try
		{
			fixed (char* pathPointer = path)
			{
				return ((delegate* unmanaged[Stdcall]<void*, char*, HRESULT>)export)(owner.Value, pathPointer);
			}
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	/// <summary>
	/// Opens the elevated Windows advanced sharing editor for a folder.
	/// </summary>
	/// <param name="owner">The window that owns the editor.</param>
	/// <param name="path">The local folder path to edit.</param>
	/// <returns>The result returned by the Windows sharing provider.</returns>
	public static HRESULT ShowAdvancedSharing(HWND owner, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		void* elevationFactory = null;
		void* sharingManager = null;
		var factoryClassId = _multiObjectElevationFactoryClassId;
		var factoryInterfaceId = _multiObjectElevationFactoryInterfaceId;
		var result = (HRESULT)PInvoke.CoCreateInstanceRaw(&factoryClassId, nint.Zero, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &factoryInterfaceId, (nint*)&elevationFactory);
		if (result.Failed || elevationFactory is null)
		{
			Release(elevationFactory);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		try
		{
			var sharingElevatedFactoryClassId = _sharingElevatedFactoryClassId;
			var prepareElevation = (delegate* unmanaged[Stdcall]<void*, void*, Guid*, HRESULT>)GetVtable(elevationFactory)[3];
			result = prepareElevation(elevationFactory, owner.Value, &sharingElevatedFactoryClassId);
			if (result.Failed)
			{
				return result;
			}

			var sharingManagerClassId = _sharingConfigurationManagerClassId;
			var sharingManagerInterfaceId = _sharingConfigurationManagerInterfaceId;
			var createElevatedInstance = (delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, void**, HRESULT>)GetVtable(elevationFactory)[5];
			result = createElevatedInstance(elevationFactory, &sharingManagerClassId, &sharingManagerInterfaceId, &sharingManager);
			if (result.Failed || sharingManager is null)
			{
				return result.Failed ? result : HRESULT.E_FAIL;
			}

			fixed (char* pathPointer = path)
			{
				var showAdvancedSharing = (delegate* unmanaged[Stdcall]<void*, void*, char*, HRESULT>)GetVtable(sharingManager)[10];

				return showAdvancedSharing(sharingManager, owner.Value, pathPointer);
			}
		}
		finally
		{
			Release(sharingManager);
			Release(elevationFactory);
		}
	}

	/// <summary>
	/// Opens the Advanced page in Network and Sharing Center.
	/// </summary>
	/// <returns>The result returned by the Windows Control Panel host.</returns>
	public static HRESULT OpenNetworkAndSharingCenter()
	{
		void* controlPanel = null;
		var classId = _openControlPanelClassId;
		var interfaceId = _openControlPanelInterfaceId;
		var result = (HRESULT)PInvoke.CoCreateInstanceRaw(&classId, nint.Zero, (uint)CLSCTX.CLSCTX_ALL, &interfaceId, (nint*)&controlPanel);
		if (result.Failed || controlPanel is null)
		{
			Release(controlPanel);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		try
		{
			fixed (char* namePointer = NetworkAndSharingCenterCanonicalName)
			fixed (char* pagePointer = NetworkAndSharingCenterPage)
			{
				var open = (delegate* unmanaged[Stdcall]<void*, char*, char*, void*, HRESULT>)GetVtable(controlPanel)[3];

				return open(controlPanel, namePointer, pagePointer, null);
			}
		}
		finally
		{
			Release(controlPanel);
		}
	}

	internal static bool CanShowPropertyPage(string path)
	{
		if (!Directory.Exists(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
		{
			return false;
		}

		var root = Path.GetPathRoot(path);
		if (root is null || PInvoke.GetDriveType(root) is not DriveRemovable and not DriveFixed and not DriveCdRom)
		{
			return false;
		}

		return ReadLocalDiskShares().Count > 0 && CanShareFolder(path);
	}

	internal static WindowsShellSharingProperties ReadProperties(string path)
	{
		var normalizedPath = NormalizePath(path);
		var bestShareName = string.Empty;
		var bestSharePath = string.Empty;
		foreach (var share in ReadLocalDiskShares())
		{
			if (share.Path.Length > bestSharePath.Length && IsPathWithin(normalizedPath, share.Path))
			{
				bestSharePath = share.Path;
				bestShareName = share.Name;
			}
		}

		var displayName = GetDisplayName(path);
		var showPasswordProtection = !IsDomainMember();
		var isPasswordProtectionEnabled = !showPasswordProtection || !IsGuestAccountEnabled();
		if (string.IsNullOrEmpty(bestSharePath))
		{
			return new(path, displayName, false, string.Empty, string.Empty, showPasswordProtection, isPasswordProtectionEnabled);
		}

		var relativePath = normalizedPath[bestSharePath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var networkPath = $"\\\\{Environment.MachineName}\\{bestShareName}";
		if (!string.IsNullOrEmpty(relativePath))
		{
			networkPath = Path.Combine(networkPath, relativePath);
		}

		return new(path, displayName, true, bestShareName, networkPath, showPasswordProtection, isPasswordProtectionEnabled);
	}

	private static bool CanShareFolder(string path)
	{
		if (!TryLoadExport(NetworkSharingLibrary, "CanShareFolder", out var module, out var export))
		{
			return false;
		}

		try
		{
			fixed (char* pathPointer = path)
			{
				return ((delegate* unmanaged[Stdcall]<char*, HRESULT>)export)(pathPointer) == HRESULT.S_OK;
			}
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	private static string GetDisplayName(string path)
	{
		if (PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem shellItem).Succeeded)
		{
			var shellName = ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_PARENTRELATIVEFORUI) ?? ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_NORMALDISPLAY);
			if (!string.IsNullOrWhiteSpace(shellName))
			{
				return shellName;
			}
		}

		var displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

		return string.IsNullOrWhiteSpace(displayName) ? path : displayName;
	}

	private static void** GetVtable(void* instance)
	{
		return *(void***)instance;
	}

	private static bool IsDiskShare(SHARE_TYPE type)
	{
		return (type & ~SHARE_TYPE.STYPE_TEMPORARY) is SHARE_TYPE.STYPE_DISKTREE;
	}

	private static bool IsDomainMember()
	{
		if (!TryLoadExport(ShellLightweightUtilityLibrary, "IsOS", out var module, out var export))
		{
			return false;
		}

		try
		{
			return ((delegate* unmanaged[Stdcall]<uint, BOOL>)export)(OsDomainMember);
		}
		finally
		{
			NativeLibrary.Free(module);
		}
	}

	private static bool IsGuestAccountEnabled()
	{
		uint resumeHandle = 0;
		do
		{
			byte* buffer = null;
			var result = PInvoke.NetUserEnum(null, 23, NET_USER_ENUM_FILTER_FLAGS.FILTER_NORMAL_ACCOUNT, out buffer, MaximumPreferredLength, out var entriesRead, out _, ref resumeHandle);
			try
			{
				if (buffer is null || result is not 0 and not ErrorMoreData)
				{
					break;
				}

				var users = (USER_INFO_23*)buffer;
				for (var index = 0u; index < entriesRead; index++)
				{
					if (!users[index].usri23_user_sid.IsNull && PInvoke.IsWellKnownSid(users[index].usri23_user_sid, WELL_KNOWN_SID_TYPE.WinAccountGuestSid))
					{
						var loggedOn = PInvoke.LogonUser(users[index].usri23_name.ToString(), ".", string.Empty, LOGON32_LOGON.LOGON32_LOGON_NETWORK, LOGON32_PROVIDER.LOGON32_PROVIDER_DEFAULT, out var guestToken);
						using (guestToken)
						{
							return loggedOn;
						}
					}
				}
			}
			finally
			{
				if (buffer is not null)
				{
					PInvoke.NetApiBufferFree(buffer);
				}
			}

			if (result is not ErrorMoreData)
			{
				break;
			}
		}
		while (true);

		return false;
	}

	private static bool IsPathWithin(string path, string root)
	{
		return !string.IsNullOrEmpty(root) && (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizePath(string path)
	{
		return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static List<LocalShare> ReadLocalDiskShares()
	{
		var shares = new List<LocalShare>();
		uint resumeHandle = 0;
		do
		{
			byte* buffer = null;
			var result = PInvoke.NetShareEnum(default, 503, out buffer, MaximumPreferredLength, out var entriesRead, out _, ref resumeHandle);
			try
			{
				if (buffer is null || result is not 0 and not ErrorMoreData)
				{
					break;
				}

				var shareEntries = (SHARE_INFO_503*)buffer;
				for (var index = 0u; index < entriesRead; index++)
				{
					var sharePath = NormalizePath(shareEntries[index].shi503_path.ToString());
					if (IsDiskShare(shareEntries[index].shi503_type) && !string.IsNullOrEmpty(sharePath))
					{
						shares.Add(new(shareEntries[index].shi503_netname.ToString(), sharePath));
					}
				}
			}
			finally
			{
				if (buffer is not null)
				{
					PInvoke.NetApiBufferFree(buffer);
				}
			}

			if (result is not ErrorMoreData)
			{
				break;
			}
		}
		while (true);

		return shares;
	}

	private static void Release(void* instance)
	{
		if (instance is null)
		{
			return;
		}

		var release = (delegate* unmanaged[Stdcall]<void*, uint>)GetVtable(instance)[2];
		release(instance);
	}

	private static bool TryLoadExport(string library, string name, out nint module, out nint export)
	{
		export = 0;
		if (!NativeLibrary.TryLoad(library, typeof(WindowsShellSharingService).Assembly, DllImportSearchPath.System32, out module))
		{
			return false;
		}

		if (NativeLibrary.TryGetExport(module, name, out export))
		{
			return true;
		}

		NativeLibrary.Free(module);
		module = 0;

		return false;
	}

	private readonly record struct LocalShare(string Name, string Path);
}

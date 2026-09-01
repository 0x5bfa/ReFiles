// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices.Marshalling;
using Files.Core.Interop.Windows;
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
	private const string NetworkAndSharingCenterCanonicalName = "Microsoft.NetworkAndSharingCenter";
	private const string NetworkAndSharingCenterPage = "Advanced";
	private static readonly Guid _multiObjectElevationFactoryClassId = new("36F0BD14-D84D-468C-B79C-9990F3FA897F");
	private static readonly Guid _openControlPanelClassId = new("06622D85-6856-4460-8DE1-A81921B41C4B");
	private static readonly Guid _sharingConfigurationManagerClassId = new("49F371E1-8C5C-4D9C-9A3B-54A6827F513C");
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

		try
		{
			return PInvoke.ShowShareFolderUI(owner, path);
		}
		catch (DllNotFoundException)
		{
			return HRESULT.E_FAIL;
		}
		catch (EntryPointNotFoundException)
		{
			return HRESULT.E_FAIL;
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

		var result = ComActivationNativeMethods.CoCreateInstance(_multiObjectElevationFactoryClassId, CLSCTX.CLSCTX_INPROC_SERVER, out IMultiObjectElevationFactory? elevationFactory);
		if (result.Failed || elevationFactory is null)
		{
			ReleaseComObject(elevationFactory);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		ISharingConfigurationUI? sharingManager = null;
		try
		{
			result = elevationFactory.Initialize(owner, in _sharingElevatedFactoryClassId);
			if (result.Failed)
			{
				return result;
			}

			var sharingManagerInterfaceId = typeof(ISharingConfigurationUI).GUID;
			result = elevationFactory.CreateElevatedObject(in _sharingConfigurationManagerClassId, in sharingManagerInterfaceId, out sharingManager);
			if (result.Failed || sharingManager is null)
			{
				return result.Failed ? result : HRESULT.E_FAIL;
			}

			return sharingManager.ShowAdvancedSharingConfigDialog(owner, path);
		}
		finally
		{
			ReleaseComObject(sharingManager);
			ReleaseComObject(elevationFactory);
		}
	}

	/// <summary>
	/// Opens the Advanced page in Network and Sharing Center.
	/// </summary>
	/// <returns>The result returned by the Windows Control Panel host.</returns>
	public static HRESULT OpenNetworkAndSharingCenter()
	{
		var result = ComActivationNativeMethods.CoCreateInstance(_openControlPanelClassId, CLSCTX.CLSCTX_ALL, out IOpenControlPanel? controlPanel);
		if (result.Failed || controlPanel is null)
		{
			ReleaseComObject(controlPanel);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		try
		{
			return controlPanel.Open(NetworkAndSharingCenterCanonicalName, NetworkAndSharingCenterPage, null);
		}
		finally
		{
			ReleaseComObject(controlPanel);
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
		try
		{
			return PInvoke.CanShareFolder(path) == HRESULT.S_OK;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
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

	private static bool IsDiskShare(SHARE_TYPE type)
	{
		return (type & ~SHARE_TYPE.STYPE_TEMPORARY) is SHARE_TYPE.STYPE_DISKTREE;
	}

	private static bool IsDomainMember()
	{
		try
		{
			return PInvoke.IsOS(OS.OS_DOMAINMEMBER);
		}
		catch (DllNotFoundException)
		{
			return false;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
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

	private static void ReleaseComObject(object? instance)
	{
		if (instance is ComObject comObject)
		{
			comObject.FinalRelease();
		}
	}

	private readonly record struct LocalShare(string Name, string Path);
}

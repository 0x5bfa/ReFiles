// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellPropertyPageEnumerator
{
	private const uint DriveNoRootDirectory = 1;
	private const uint DriveRemote = 4;
	private const uint DriveCdRom = 5;
	private const uint FilePersistentAcls = 0x00000008;
	private const uint FileVolumeQuotas = 0x00000020;
	private static readonly HashSet<string> _signatureExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".appx",
		".cat",
		".cpl",
		".dll",
		".exe",
		".lnk",
		".msi",
		".msix",
		".ocx",
		".scr",
		".sys",
	};

	internal static IReadOnlyList<WindowsShellPropertyPage> GetPages(WindowsShellResolvedSelection selection)
	{
		ArgumentNullException.ThrowIfNull(selection);

		var pages = new List<WindowsShellPropertyPage>
		{
			new(WindowsShellPropertyPageKind.General, string.Empty, true),
		};
		var primaryPath = selection.FileSystemPaths.Count is 1 ? selection.FileSystemPaths[0] : null;
		if (primaryPath is not null && TryGetDriveRoot(primaryPath, out var driveRoot))
		{
			AddDrivePages(pages, driveRoot);

			return pages;
		}

		var isShortcut = primaryPath is not null && Path.GetExtension(primaryPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
		if (isShortcut)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Shortcut, string.Empty, false));
		}

		if (primaryPath is not null && CanShowCompatibility(primaryPath))
		{
			pages.Add(new(WindowsShellPropertyPageKind.Compatibility, string.Empty, false));
		}

		if (selection.IsSingleFolder)
		{
			if (primaryPath is not null && CanShare(primaryPath))
			{
				pages.Add(new(WindowsShellPropertyPageKind.Sharing, string.Empty, false));
			}

			if (primaryPath is not null && SupportsPersistentAcls(primaryPath))
			{
				pages.Add(new(WindowsShellPropertyPageKind.Security, string.Empty, false));
			}

			if (primaryPath is not null && CanShowPreviousVersions(primaryPath))
			{
				pages.Add(new(WindowsShellPropertyPageKind.PreviousVersions, string.Empty, false));
			}

			if (primaryPath is not null && CanCustomize(primaryPath))
			{
				pages.Add(new(WindowsShellPropertyPageKind.Customize, string.Empty, false));
			}

			return pages;
		}

		if (primaryPath is not null && _signatureExtensions.Contains(Path.GetExtension(primaryPath)))
		{
			pages.Add(new(WindowsShellPropertyPageKind.DigitalSignatures, string.Empty, false));
		}

		if (primaryPath is not null && SupportsPersistentAcls(primaryPath))
		{
			pages.Add(new(WindowsShellPropertyPageKind.Security, string.Empty, false));
		}

		pages.Add(new(WindowsShellPropertyPageKind.Details, string.Empty, false));
		if (primaryPath is not null && CanShowPreviousVersions(primaryPath))
		{
			pages.Add(new(WindowsShellPropertyPageKind.PreviousVersions, string.Empty, false));
		}

		return pages;
	}

	internal static bool TryGetDriveRoot(string path, out string root)
	{
		root = string.Empty;
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		try
		{
			var fullPath = Path.GetFullPath(path);
			var candidate = Path.GetPathRoot(fullPath);
			if (candidate is null
				|| candidate.Length < 3
				|| candidate[1] is not ':'
				|| !Path.TrimEndingDirectorySeparator(fullPath).Equals(Path.TrimEndingDirectorySeparator(candidate), StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			root = Path.EndsInDirectorySeparator(candidate) ? candidate : candidate + Path.DirectorySeparatorChar;

			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
		{
			return false;
		}
	}

	private static void AddDrivePages(List<WindowsShellPropertyPage> pages, string root)
	{
		var driveType = PInvoke.GetDriveType(root);
		var hasMountedVolume = driveType is not DriveNoRootDirectory and not DriveRemote;
		TryGetVolumeFlags(root, out var fileSystemFlags);
		if (hasMountedVolume && driveType is not DriveCdRom)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Tools, string.Empty, false));
		}

		if (hasMountedVolume && !IsWow64Process() && PInvoke.SHRestricted(RESTRICTIONS.REST_NOHARDWARETAB) is 0)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Hardware, string.Empty, false));
		}

		if (CanShare(root))
		{
			pages.Add(new(WindowsShellPropertyPageKind.Sharing, string.Empty, false));
		}

		if ((fileSystemFlags & FilePersistentAcls) is not 0)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Security, string.Empty, false));
		}

		if (hasMountedVolume && driveType is not DriveCdRom && CanShowPreviousVersions(root))
		{
			pages.Add(new(WindowsShellPropertyPageKind.PreviousVersions, string.Empty, false));
		}

		if ((fileSystemFlags & FileVolumeQuotas) is not 0)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Quota, string.Empty, false));
		}

		if (CanCustomize(root))
		{
			pages.Add(new(WindowsShellPropertyPageKind.Customize, string.Empty, false));
		}
	}

	private static unsafe bool CanCustomize(string path)
	{
		if (!Directory.Exists(path))
		{
			return false;
		}

		if (PInvoke.SHRestricted(RESTRICTIONS.REST_NOCUSTOMIZETHISFOLDER) is not 0 || PInvoke.SHRestricted(RESTRICTIONS.REST_CLASSICSHELL) is not 0
			|| PInvoke.SHRestricted(RESTRICTIONS.REST_NOCUSTOMIZEWEBVIEW) is not 0)
		{
			return false;
		}

		var isDriveRoot = TryGetDriveRoot(path, out var root);
		if (isDriveRoot)
		{
			var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
			if (systemRoot is not null && Path.TrimEndingDirectorySeparator(root).Equals(Path.TrimEndingDirectorySeparator(systemRoot), StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		var parsingName = isDriveRoot ? root : path;
		var isOpticalDrive = isDriveRoot && PInvoke.GetDriveType(root) is DriveCdRom;
		if (PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem shellItem).Failed)
		{
			return isOpticalDrive;
		}

		const SFGAO_FLAGS mask = SFGAO_FLAGS.SFGAO_FILESYSANCESTOR | SFGAO_FLAGS.SFGAO_FOLDER | SFGAO_FLAGS.SFGAO_FILESYSTEM | SFGAO_FLAGS.SFGAO_READONLY;
		const SFGAO_FLAGS required = SFGAO_FLAGS.SFGAO_FILESYSANCESTOR | SFGAO_FLAGS.SFGAO_FOLDER | SFGAO_FLAGS.SFGAO_FILESYSTEM;

		return (shellItem.GetAttributes(mask, out var attributes).Succeeded && (attributes & mask) == required) || isOpticalDrive;
	}

	private static bool CanShowCompatibility(string path)
	{
		var extension = Path.GetExtension(path);
		var executablePath = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? WindowsShellPropertySheetReader.TryResolveShortcutTarget(path) : path;

		return executablePath is not null
			&& Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
			&& File.Exists(executablePath)
			&& PInvoke.GetBinaryType(executablePath, out _);
	}

	private static bool CanShare(string path)
	{
		return WindowsShellSharingService.CanShowPropertyPage(path);
	}

	private static bool CanShowPreviousVersions(string path)
	{
		if (!File.Exists(path) && !Directory.Exists(path))
		{
			return false;
		}

		return !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static segment => segment.StartsWith("@GMT-", StringComparison.OrdinalIgnoreCase));
	}

	private static bool SupportsPersistentAcls(string path)
	{
		var root = Path.GetPathRoot(path);

		return root is not null && TryGetVolumeFlags(root, out var fileSystemFlags) && (fileSystemFlags & FilePersistentAcls) is not 0;
	}

	private static bool TryGetVolumeFlags(string root, out uint fileSystemFlags)
	{
		fileSystemFlags = 0;

		return PInvoke.GetVolumeInformation(root, [], out _, out _, out fileSystemFlags, []);
	}

	private static unsafe bool IsWow64Process()
	{
		var processMachine = default(IMAGE_FILE_MACHINE);
		var nativeMachine = default(IMAGE_FILE_MACHINE);
		if (!PInvoke.IsWow64Process2(PInvoke.GetCurrentProcess(), &processMachine, &nativeMachine))
		{
			return false;
		}

		return (ushort)processMachine is not 0;
	}
}

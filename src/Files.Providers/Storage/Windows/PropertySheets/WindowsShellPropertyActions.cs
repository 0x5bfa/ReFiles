// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Implements Windows property actions behind consumer contracts.</summary>
public sealed class WindowsShellPropertyActions : IWindowsShellPropertyActions
{
	/// <inheritdoc />
	public WindowsShellOperationResult ShowErrorChecking(nint owner, string rootPath)
	{
		return WindowsShellDriveToolsService.ShowErrorChecking(owner, rootPath);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowOptimization(nint owner, string rootPath)
	{
		return WindowsShellDriveToolsService.ShowOptimization(owner, rootPath);
	}

	/// <inheritdoc />
	public bool TryPickIcon(nint owner, string initialPath, int initialIndex, out string iconPath, out int iconIndex)
	{
		return WindowsShellFolderCustomizationService.TryPickIcon(owner, initialPath, initialIndex, out iconPath, out iconIndex);
	}

	/// <inheritdoc />
	public void Apply(string folderPath, string folderKind, bool folderKindChanged, bool applyToSubfolders, string picturePath, bool pictureChanged, string iconPath,
		int iconIndex, bool iconChanged)
	{
		WindowsShellFolderCustomizationService.Apply(folderPath, folderKind, folderKindChanged, applyToSubfolders, picturePath, pictureChanged, iconPath, iconIndex, iconChanged);
	}

	/// <inheritdoc />
	public int ShowProperties(nint ownerWindowHandle, string deviceInstanceId)
	{
		return WindowsShellHardwareService.ShowProperties(ownerWindowHandle, deviceInstanceId);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowSettings(nint owner, string rootPath, string displayName)
	{
		return WindowsShellQuotaService.ShowSettings(owner, rootPath, displayName);
	}

	/// <inheritdoc />
	public bool RequiresElevation(string path)
	{
		return WindowsShellSecurityService.RequiresElevation(path);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowPermissionsEditor(nint owner, string path)
	{
		return WindowsShellSecurityService.ShowPermissionsEditor(owner, path);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowAdvancedEditor(nint owner, string path)
	{
		return WindowsShellSecurityService.ShowAdvancedEditor(owner, path);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowSharingWizard(nint owner, string path)
	{
		return WindowsShellSharingService.ShowSharingWizard(owner, path);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult ShowAdvancedSharing(nint owner, string path)
	{
		return WindowsShellSharingService.ShowAdvancedSharing(owner, path);
	}

	/// <inheritdoc />
	public WindowsShellOperationResult OpenNetworkAndSharingCenter()
	{
		return WindowsShellSharingService.OpenNetworkAndSharingCenter();
	}

	/// <inheritdoc />
	public WindowsShellOperationResult OpenDriveUsage(string rootPath)
	{
		return WindowsShellStorageSettingsService.OpenDriveUsage(rootPath);
	}

	/// <inheritdoc />
	public bool SupportsDriveUsage(string rootPath)
	{
		return WindowsShellStorageSettingsService.SupportsDriveUsage(rootPath);
	}

	/// <inheritdoc />
	public string GetDisplayName(string rootPath)
	{
		return WindowsVolumeLabelService.GetDisplayName(rootPath);
	}

	/// <inheritdoc />
	public void SetLabel(nint ownerWindowHandle, string rootPath, string label)
	{
		WindowsVolumeLabelService.SetLabel(ownerWindowHandle, rootPath, label);
	}
}

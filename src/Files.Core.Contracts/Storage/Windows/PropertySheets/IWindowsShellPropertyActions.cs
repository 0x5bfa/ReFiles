// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Invokes explicitly requested Windows property editors and updates.</summary>
public interface IWindowsShellPropertyActions
{
	/// <summary>
	/// Starts an elevated online filesystem check for a volume.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns the elevation prompt.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <returns>The status representing the launch result.</returns>
	WindowsShellOperationResult ShowErrorChecking(nint owner, string rootPath);

	/// <summary>
	/// Opens the Windows drive optimization application for a volume.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns any launch UI.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <returns>The status representing the launch result.</returns>
	WindowsShellOperationResult ShowOptimization(nint owner, string rootPath);

	/// <summary>
	/// Shows the Shell icon picker.
	/// </summary>
	/// <param name="owner">The borrowed owner window handle.</param>
	/// <param name="initialPath">The initial icon resource path.</param>
	/// <param name="initialIndex">The initial icon resource index.</param>
	/// <param name="iconPath">Receives the selected icon resource path.</param>
	/// <param name="iconIndex">Receives the selected icon resource index.</param>
	/// <returns><see langword="true"/> when the user selected an icon.</returns>
	bool TryPickIcon(nint owner, string initialPath, int initialIndex, out string iconPath, out int iconIndex);

	/// <summary>
	/// Applies staged customization values to a folder.
	/// </summary>
	/// <param name="folderPath">The folder path.</param>
	/// <param name="folderKind">The canonical folder kind.</param>
	/// <param name="folderKindChanged">Whether the folder kind changed.</param>
	/// <param name="applyToSubfolders">Whether descendants should inherit the selected folder kind.</param>
	/// <param name="picturePath">The folder picture path, or an empty string to restore the default.</param>
	/// <param name="pictureChanged">Whether the folder picture changed.</param>
	/// <param name="iconPath">The folder icon resource path.</param>
	/// <param name="iconIndex">The folder icon resource index.</param>
	/// <param name="iconChanged">Whether the folder icon changed.</param>
	void Apply(string folderPath, string folderKind, bool folderKindChanged, bool applyToSubfolders, string picturePath, bool pictureChanged, string iconPath,
		int iconIndex, bool iconChanged);

	/// <summary>Shows device properties using the caller's window as a borrowed owner.</summary>
	/// <param name="ownerWindowHandle">The borrowed owner window handle.</param>
	/// <param name="deviceInstanceId">The device instance identifier.</param>
	/// <returns>The Win32 error code, or zero on success.</returns>
	int ShowProperties(nint ownerWindowHandle, string deviceInstanceId);

	/// <summary>
	/// Opens the quota editor for a volume through the Shell elevation helper.
	/// </summary>
	/// <param name="owner">The window that owns the editor and elevation prompt.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <param name="displayName">The Shell display name for the volume.</param>
	/// <returns>The HRESULT returned by the quota UI helper.</returns>
	WindowsShellOperationResult ShowSettings(nint owner, string rootPath, string displayName);

	/// <summary>
	/// Determines whether editing an object's DACL requires elevation for the current token.
	/// </summary>
	/// <param name="path">The file-system path to inspect.</param>
	/// <returns><see langword="true"/> when the permissions editor must be elevated.</returns>
	bool RequiresElevation(string path);

	/// <summary>
	/// Opens the permissions editor, requesting elevation only when the current token cannot read and write the DACL.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns the editor and any elevation prompt.</param>
	/// <param name="path">The file-system path whose permissions will be edited.</param>
	/// <returns>The HRESULT returned by the Shell security extension.</returns>
	WindowsShellOperationResult ShowPermissionsEditor(nint owner, string path);

	/// <summary>
	/// Opens the advanced permissions editor through the normal Shell security provider.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns the editor.</param>
	/// <param name="path">The file-system path whose permissions will be displayed.</param>
	/// <returns>The HRESULT returned by the Shell security extension.</returns>
	WindowsShellOperationResult ShowAdvancedEditor(nint owner, string path);

	/// <summary>
	/// Opens the Windows sharing wizard for a folder.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns the wizard.</param>
	/// <param name="path">The local folder path to share.</param>
	/// <returns>The result returned by the Windows sharing provider.</returns>
	WindowsShellOperationResult ShowSharingWizard(nint owner, string path);

	/// <summary>
	/// Opens the elevated Windows advanced sharing editor for a folder.
	/// </summary>
	/// <param name="owner">The borrowed window handle that owns the editor.</param>
	/// <param name="path">The local folder path to edit.</param>
	/// <returns>The result returned by the Windows sharing provider.</returns>
	WindowsShellOperationResult ShowAdvancedSharing(nint owner, string path);

	/// <summary>
	/// Opens the Advanced page in Network and Sharing Center.
	/// </summary>
	/// <returns>The result returned by the Windows Control Panel host.</returns>
	WindowsShellOperationResult OpenNetworkAndSharingCenter();

	/// <summary>
	/// Opens the storage-usage details for a drive.
	/// </summary>
	/// <param name="rootPath">The drive root to select.</param>
	/// <returns>The HRESULT returned by application activation.</returns>
	WindowsShellOperationResult OpenDriveUsage(string rootPath);

	/// <summary>
	/// Determines whether Explorer exposes storage-usage details for a drive.
	/// </summary>
	/// <param name="rootPath">The drive root to inspect.</param>
	/// <returns><see langword="true"/> when the drive is present in the Storage Sense inventory.</returns>
	bool SupportsDriveUsage(string rootPath);

	/// <summary>
	/// Gets the localized Shell display name for a drive root.
	/// </summary>
	/// <param name="rootPath">The drive root path.</param>
	/// <returns>The localized Shell display name, including the drive designator when available.</returns>
	string GetDisplayName(string rootPath);

	/// <summary>
	/// Changes or clears a filesystem volume label and notifies the Shell.
	/// </summary>
	/// <param name="ownerWindowHandle">The borrowed window handle that owns the permission dialog and elevation prompt.</param>
	/// <param name="rootPath">The drive root path.</param>
	/// <param name="label">The new volume label, or an empty string to clear it.</param>
	void SetLabel(nint ownerWindowHandle, string rootPath, string label);
}

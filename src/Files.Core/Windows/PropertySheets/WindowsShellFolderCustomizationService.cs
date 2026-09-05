// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Windows;

/// <summary>
/// Applies folder customization using the same Shell property bags and customization API used by Explorer.
/// </summary>
public static unsafe class WindowsShellFolderCustomizationService
{
	private const uint InheritedPropertyBagFlags = 0x00000010;
	private const uint GenericWrite = 0x80000000;
	private const int MaximumPathLength = 260;
	private const uint ProfileSectionReadWriteMode = 2;
	private const string ShellPropertyBagName = "Shell";
	private const string ViewStateSectionName = "ViewState";

	/// <summary>
	/// Shows the Shell icon picker.
	/// </summary>
	/// <param name="owner">The owner window.</param>
	/// <param name="initialPath">The initial icon resource path.</param>
	/// <param name="initialIndex">The initial icon resource index.</param>
	/// <param name="iconPath">Receives the selected icon resource path.</param>
	/// <param name="iconIndex">Receives the selected icon resource index.</param>
	/// <returns><see langword="true"/> when the user selected an icon.</returns>
	public static bool TryPickIcon(HWND owner, string initialPath, int initialIndex, out string iconPath, out int iconIndex)
	{
		ArgumentNullException.ThrowIfNull(initialPath);

		Span<char> pathBuffer = stackalloc char[MaximumPathLength];
		pathBuffer.Clear();
		initialPath.AsSpan(0, Math.Min(initialPath.Length, pathBuffer.Length - 1)).CopyTo(pathBuffer);
		var selectedIndex = initialIndex;
		var selected = PInvoke.PickIconDlg(owner, ref pathBuffer, checked((uint)pathBuffer.Length), ref selectedIndex) is not 0;
		var terminatorIndex = pathBuffer.IndexOf('\0');
		iconPath = selected ? pathBuffer[..(terminatorIndex < 0 ? pathBuffer.Length : terminatorIndex)].ToString() : initialPath;
		iconIndex = selected ? selectedIndex : initialIndex;

		return selected;
	}

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
	public static void Apply(string folderPath, string folderKind, bool folderKindChanged, bool applyToSubfolders, string picturePath, bool pictureChanged, string iconPath,
		int iconIndex, bool iconChanged)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
		ArgumentNullException.ThrowIfNull(folderKind);
		ArgumentNullException.ThrowIfNull(picturePath);
		ArgumentNullException.ThrowIfNull(iconPath);

		if (folderKindChanged)
		{
			var desktopResult = WriteDesktopFolderKind(folderPath, folderKind);
			if (desktopResult.Succeeded)
			{
				TouchDirectory(folderPath);
			}

			var inheritedResult = UpdateInheritedFolderKind(folderPath, folderKind, applyToSubfolders || desktopResult.Failed);
			if (desktopResult.Failed && inheritedResult.Failed)
			{
				desktopResult.ThrowOnFailure();
			}
		}

		if (pictureChanged || iconChanged)
		{
			WriteFolderAppearance(folderPath, picturePath, pictureChanged, iconPath, iconIndex, iconChanged);
		}
	}

	internal static bool IsFolderKindInherited(string folderPath, string folderKind)
	{
		var hr = CreateViewStatePropertyBag(folderPath, out var propertyBag);
		if (hr.Failed || propertyBag is null)
		{
			return false;
		}

		Span<char> value = stackalloc char[MaximumPathLength];
		hr = PInvoke.PSPropertyBag_ReadStr(propertyBag, "FolderType", value);

		return hr.Succeeded && value[0] is not '\0' && value.TrimEnd('\0').Equals(NormalizeFolderKind(folderKind), StringComparison.OrdinalIgnoreCase);
	}

	internal static string ReadFolderKind(string folderPath, string fallback)
	{
		HRESULT hr;
		IPropertyBag? propertyBag;
		try
		{
			hr = CreateDesktopPropertyBag(folderPath, out propertyBag);
		}
		catch (EntryPointNotFoundException)
		{
			return fallback;
		}

		if (hr.Failed || propertyBag is null)
		{
			return fallback;
		}

		Span<char> value = stackalloc char[MaximumPathLength];
		hr = PInvoke.PSPropertyBag_ReadStr(propertyBag, "FolderType", value);

		return hr.Succeeded && value[0] is not '\0' ? value.TrimEnd('\0').ToString() : fallback;
	}

	private static HRESULT CreateDesktopPropertyBag(string folderPath, out IPropertyBag? propertyBag)
	{
		propertyBag = null;
		if (PInvoke.IsPathOwnedByCurrentUser(folderPath) is 0)
		{
			return (HRESULT)unchecked((int)0x80070005);
		}

		ITEMIDLIST* absolutePidl = null;
		var hr = PInvoke.SHParseDisplayName(folderPath, null, out absolutePidl, 0, out _);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return hr.Failed ? hr : HRESULT.E_FAIL;
		}

		try
		{
			hr = PInvoke.GetCachedIniForFolder(0, in *absolutePidl, 0, out var cachedProfile);
			if (hr.Failed || cachedProfile is null)
			{
				return hr.Failed ? hr : HRESULT.E_NOINTERFACE;
			}

			var propertyBagId = typeof(IPropertyBag).GUID;
			hr = PInvoke.SHCreatePropertyBagOnCachedProfileSection(cachedProfile, ViewStateSectionName, ProfileSectionReadWriteMode, in propertyBagId, out propertyBag);

			return hr.Failed || propertyBag is not null ? hr : HRESULT.E_NOINTERFACE;
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static HRESULT CreateViewStatePropertyBag(string folderPath, out IPropertyBag? propertyBag)
	{
		propertyBag = null;
		ITEMIDLIST* absolutePidl = null;
		var hr = PInvoke.SHParseDisplayName(folderPath, null, out absolutePidl, 0, out _);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return hr.Failed ? hr : HRESULT.E_FAIL;
		}

		try
		{
			var propertyBagId = typeof(IPropertyBag).GUID;
			hr = PInvoke.SHGetViewStatePropertyBag(in *absolutePidl, ShellPropertyBagName, InheritedPropertyBagFlags, in propertyBagId, out propertyBag);

			return hr.Failed || propertyBag is not null ? hr : HRESULT.E_NOINTERFACE;
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static void DeleteCustomization(IPropertyBag propertyBag)
	{
		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "FolderType");
		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "Logo");
		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "Mode");
		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "Vid");
	}

	private static string NormalizeFolderKind(string folderKind)
	{
		return string.IsNullOrWhiteSpace(folderKind) ? "Generic" : folderKind;
	}

	private static HRESULT UpdateInheritedFolderKind(string folderPath, string folderKind, bool applyToSubfolders)
	{
		var hr = CreateViewStatePropertyBag(folderPath, out var propertyBag);
		if (hr.Failed || propertyBag is null)
		{
			return hr.Failed ? hr : HRESULT.E_FAIL;
		}

		DeleteCustomization(propertyBag);
		if (!applyToSubfolders)
		{
			return HRESULT.S_OK;
		}

		hr = PInvoke.PSPropertyBag_WriteStr(propertyBag, "FolderType", NormalizeFolderKind(folderKind));

		return hr;
	}

	private static void TouchDirectory(string folderPath)
	{
		using var directory = PInvoke.CreateFile(
			folderPath,
			GenericWrite | (uint)FILE_ACCESS_RIGHTS.FILE_WRITE_ATTRIBUTES,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_DELETE,
			null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
			null);
		if (!directory.IsInvalid)
		{
			var fileTimeValue = DateTime.UtcNow.ToFileTimeUtc();
			var fileTime = new System.Runtime.InteropServices.ComTypes.FILETIME { dwLowDateTime = (int)fileTimeValue, dwHighDateTime = (int)(fileTimeValue >> 32) };
			_ = PInvoke.SetFileTime(directory, null, null, fileTime);
		}

		fixed (char* folderPathPointer = folderPath)
		{
			PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_UPDATEITEM, SHCNF_FLAGS.SHCNF_PATHW, folderPathPointer, null);
		}
	}

	private static HRESULT WriteDesktopFolderKind(string folderPath, string folderKind)
	{
		HRESULT hr;
		IPropertyBag? propertyBag;
		try
		{
			hr = CreateDesktopPropertyBag(folderPath, out propertyBag);
		}
		catch (EntryPointNotFoundException)
		{
			return HRESULT.E_NOTIMPL;
		}

		if (hr.Failed || propertyBag is null)
		{
			return hr.Failed ? hr : HRESULT.E_FAIL;
		}

		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "Mode");
		_ = PInvoke.PSPropertyBag_Delete(propertyBag, "Vid");
		hr = PInvoke.PSPropertyBag_WriteStr(propertyBag, "FolderType", NormalizeFolderKind(folderKind));

		return hr;
	}

	private static void WriteFolderAppearance(string folderPath, string picturePath, bool pictureChanged, string iconPath, int iconIndex, bool iconChanged)
	{
		fixed (char* picturePointer = picturePath)
		fixed (char* iconPointer = iconPath)
		{
			SHFOLDERCUSTOMSETTINGS settings = default;
			settings.dwSize = checked((uint)sizeof(SHFOLDERCUSTOMSETTINGS));
			settings.dwMask = (pictureChanged ? PInvoke.FCSM_LOGO : 0) | (iconChanged ? PInvoke.FCSM_ICONFILE : 0);
			settings.pszLogo = picturePointer;
			settings.pszIconFile = iconPointer;
			settings.iIconIndex = iconIndex;
			var hr = PInvoke.SHGetSetFolderCustomSettings(ref settings, folderPath, PInvoke.FCS_FORCEWRITE);
			hr.ThrowOnFailure();
		}
	}
}

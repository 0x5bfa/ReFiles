// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

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
	private static readonly Guid _cachedPrivateProfileId = new("B57046BC-32E5-428A-9887-19F712B907BF");
	private static readonly Guid _propertyBagId = new("55272A00-42CB-11CE-8135-00AA004BB851");

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
		initialPath.AsSpan(0, Math.Min(initialPath.Length, pathBuffer.Length - 1)).CopyTo(pathBuffer);
		var selectedIndex = initialIndex;
		var selected = PInvoke.PickIconDlg(owner, ref pathBuffer, checked((uint)pathBuffer.Length), ref selectedIndex) is not 0;
		iconPath = selected ? pathBuffer.ToString() : initialPath;
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
		var bagResult = CreateViewStatePropertyBag(folderPath, out var propertyBag);
		if (bagResult.Failed || propertyBag is 0)
		{
			Release(propertyBag);

			return false;
		}

		try
		{
			Span<char> value = stackalloc char[MaximumPathLength];
			fixed (char* valuePointer = value)
			{
				return PInvoke.PSPropertyBagReadStringRaw(propertyBag, "FolderType", valuePointer, checked((uint)value.Length)).Succeeded
					&& value[0] is not '\0' && value.TrimEnd('\0').Equals(NormalizeFolderKind(folderKind), StringComparison.OrdinalIgnoreCase);
			}
		}
		finally
		{
			Release(propertyBag);
		}
	}

	internal static string ReadFolderKind(string folderPath, string fallback)
	{
		HRESULT bagResult;
		nint propertyBag;
		try
		{
			bagResult = CreateDesktopPropertyBag(folderPath, out propertyBag);
		}
		catch (EntryPointNotFoundException)
		{
			return fallback;
		}

		if (bagResult.Failed || propertyBag is 0)
		{
			Release(propertyBag);

			return fallback;
		}

		try
		{
			Span<char> value = stackalloc char[MaximumPathLength];
			fixed (char* valuePointer = value)
			{
				var readResult = PInvoke.PSPropertyBagReadStringRaw(propertyBag, "FolderType", valuePointer, checked((uint)value.Length));

				return readResult.Succeeded && value[0] is not '\0' ? value.TrimEnd('\0').ToString() : fallback;
			}
		}
		finally
		{
			Release(propertyBag);
		}
	}

	private static HRESULT CreateDesktopPropertyBag(string folderPath, out nint propertyBag)
	{
		propertyBag = 0;
		if (PInvoke.IsPathOwnedByCurrentUser(folderPath) is 0)
		{
			return (HRESULT)unchecked((int)0x80070005);
		}

		ITEMIDLIST* absolutePidl = null;
		var parseResult = PInvoke.SHParseDisplayName(folderPath, null, out absolutePidl, 0, out _);
		if (parseResult.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return parseResult.Failed ? parseResult : HRESULT.E_FAIL;
		}

		nint cachedProfileUnknown = 0;
		try
		{
			var cachedResult = PInvoke.GetCachedIniForFolderRaw(0, absolutePidl, 0, &cachedProfileUnknown);
			if (cachedResult.Failed || cachedProfileUnknown is 0)
			{
				return cachedResult.Failed ? cachedResult : HRESULT.E_FAIL;
			}

			nint cachedProfile = 0;
			var queryResult = QueryInterface(cachedProfileUnknown, _cachedPrivateProfileId, out cachedProfile);
			if (queryResult.Failed || cachedProfile is 0)
			{
				Release(cachedProfile);

				return queryResult.Failed ? queryResult : HRESULT.E_FAIL;
			}

			try
			{
				var propertyBagId = _propertyBagId;
				nint createdPropertyBag = 0;
				fixed (char* sectionName = ViewStateSectionName)
				{
					var createResult = PInvoke.SHCreatePropertyBagOnCachedProfileSectionRaw(cachedProfile, sectionName, ProfileSectionReadWriteMode, &propertyBagId, &createdPropertyBag);
					propertyBag = createdPropertyBag;

					return createResult;
				}
			}
			finally
			{
				Release(cachedProfile);
			}
		}
		finally
		{
			Release(cachedProfileUnknown);
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static HRESULT CreateViewStatePropertyBag(string folderPath, out nint propertyBag)
	{
		propertyBag = 0;
		ITEMIDLIST* absolutePidl = null;
		var parseResult = PInvoke.SHParseDisplayName(folderPath, null, out absolutePidl, 0, out _);
		if (parseResult.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return parseResult.Failed ? parseResult : HRESULT.E_FAIL;
		}

		try
		{
			var propertyBagId = _propertyBagId;
			nint createdPropertyBag = 0;
			var createResult = PInvoke.SHGetViewStatePropertyBagRaw(absolutePidl, ShellPropertyBagName, InheritedPropertyBagFlags, &propertyBagId, &createdPropertyBag);
			propertyBag = createdPropertyBag;

			return createResult;
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static void DeleteCustomization(nint propertyBag)
	{
		_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "FolderType");
		_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "Logo");
		_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "Mode");
		_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "Vid");
	}

	private static string NormalizeFolderKind(string folderKind)
	{
		return string.IsNullOrWhiteSpace(folderKind) ? "Generic" : folderKind;
	}

	private static HRESULT QueryInterface(nint instance, Guid interfaceId, out nint result)
	{
		result = 0;
		var requestedInterfaceId = interfaceId;
		nint queriedInterface = 0;
		var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, HRESULT>)(*(nint**)instance)[0];
		var queryResult = queryInterface(instance, &requestedInterfaceId, &queriedInterface);
		result = queriedInterface;

		return queryResult;
	}

	private static void Release(nint instance)
	{
		if (instance is not 0)
		{
			var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2];
			release(instance);
		}
	}

	private static HRESULT UpdateInheritedFolderKind(string folderPath, string folderKind, bool applyToSubfolders)
	{
		var bagResult = CreateViewStatePropertyBag(folderPath, out var propertyBag);
		if (bagResult.Failed || propertyBag is 0)
		{
			Release(propertyBag);

			return bagResult.Failed ? bagResult : HRESULT.E_FAIL;
		}

		try
		{
			DeleteCustomization(propertyBag);

			return applyToSubfolders ? PInvoke.PSPropertyBagWriteStringRaw(propertyBag, "FolderType", NormalizeFolderKind(folderKind)) : HRESULT.S_OK;
		}
		finally
		{
			Release(propertyBag);
		}
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
		HRESULT bagResult;
		nint propertyBag;
		try
		{
			bagResult = CreateDesktopPropertyBag(folderPath, out propertyBag);
		}
		catch (EntryPointNotFoundException)
		{
			return HRESULT.E_NOTIMPL;
		}

		if (bagResult.Failed || propertyBag is 0)
		{
			Release(propertyBag);

			return bagResult.Failed ? bagResult : HRESULT.E_FAIL;
		}

		try
		{
			_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "Mode");
			_ = PInvoke.PSPropertyBagDeleteRaw(propertyBag, "Vid");

			return PInvoke.PSPropertyBagWriteStringRaw(propertyBag, "FolderType", NormalizeFolderKind(folderKind));
		}
		finally
		{
			Release(propertyBag);
		}
	}

	private static void WriteFolderAppearance(string folderPath, string picturePath, bool pictureChanged, string iconPath, int iconIndex, bool iconChanged)
	{
		fixed (char* picturePointer = picturePath)
		fixed (char* iconPointer = iconPath)
		{
			var settings = new SHFOLDERCUSTOMSETTINGS
			{
				dwSize = checked((uint)sizeof(SHFOLDERCUSTOMSETTINGS)),
				dwMask = (pictureChanged ? PInvoke.FCSM_LOGO : 0) | (iconChanged ? PInvoke.FCSM_ICONFILE : 0),
				pszLogo = new PWSTR(picturePointer),
				pszIconFile = new PWSTR(iconPointer),
				iIconIndex = iconIndex,
			};
			PInvoke.SHGetSetFolderCustomSettings(ref settings, folderPath, PInvoke.FCS_READ | PInvoke.FCS_FORCEWRITE).ThrowOnFailure();
		}
	}
}

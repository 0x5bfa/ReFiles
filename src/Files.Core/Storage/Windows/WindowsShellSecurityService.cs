// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Com;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Opens the Windows NTFS access-control editors used by the Shell security property page.
/// </summary>
public static unsafe class WindowsShellSecurityService
{
	private const uint PermissionsPage = 0;
	private const uint AdvancedPermissionsPage = 1;
	private static readonly Guid _interfaceId = new("74807F67-0058-440D-8600-65541A7FBBEA");

	/// <summary>
	/// Determines whether editing an object's DACL requires elevation for the current token.
	/// </summary>
	/// <param name="path">The file-system path to inspect.</param>
	/// <returns><see langword="true"/> when the permissions editor must be elevated.</returns>
	public static bool RequiresElevation(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		return !CanReadAndWriteDacl(path);
	}

	/// <summary>
	/// Opens the permissions editor, requesting elevation only when the current token cannot read and write the DACL.
	/// </summary>
	/// <param name="owner">The window that owns the editor and any elevation prompt.</param>
	/// <param name="path">The file-system path whose permissions will be edited.</param>
	/// <returns>The HRESULT returned by the Shell security extension.</returns>
	public static HRESULT ShowPermissionsEditor(HWND owner, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		return ShowEditor(owner, path, PermissionsPage, RequiresElevation(path));
	}

	/// <summary>
	/// Opens the advanced permissions editor through the normal Shell security provider.
	/// </summary>
	/// <param name="owner">The window that owns the editor.</param>
	/// <param name="path">The file-system path whose permissions will be displayed.</param>
	/// <returns>The HRESULT returned by the Shell security extension.</returns>
	public static HRESULT ShowAdvancedEditor(HWND owner, string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		return ShowEditor(owner, path, AdvancedPermissionsPage, false);
	}

	private static bool CanReadAndWriteDacl(string path)
	{
		using var handle = PInvoke.CreateFile(
			path,
			(uint)(FILE_ACCESS_RIGHTS.READ_CONTROL | FILE_ACCESS_RIGHTS.WRITE_DAC),
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
			null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING,
			FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
			null);

		return !handle.IsInvalid;
	}

	private static void** GetVtable(void* instance)
	{
		return *(void***)instance;
	}

	private static void Release(void* instance)
	{
		if (instance is not null)
		{
			((delegate* unmanaged[Stdcall]<void*, uint>)GetVtable(instance)[2])(instance);
		}
	}

	private static HRESULT ShowEditor(HWND owner, string path, uint page, bool elevate)
	{
		void* editor = null;
		var classId = CLSID.CLSID_NTFSSecurityExt;
		var interfaceId = _interfaceId;
		var result = elevate
			? WindowsElevationMoniker.Create(owner, classId, interfaceId, &editor)
			: (HRESULT)PInvoke.CoCreateInstanceRaw(&classId, nint.Zero, (uint)CLSCTX.CLSCTX_INPROC_SERVER, &interfaceId, (nint*)&editor);
		if (result.Failed || editor is null)
		{
			Release(editor);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		var resourceName = Marshal.StringToBSTR(path);
		var objectName = Marshal.StringToBSTR(path);
		try
		{
			var openEditor = (delegate* unmanaged[Stdcall]<void*, nint, nint, nint, int, uint, HRESULT>)GetVtable(editor)[3];

			return openEditor(editor, (nint)owner.Value, resourceName, objectName, Directory.Exists(path) ? 1 : 0, page);
		}
		finally
		{
			Marshal.FreeBSTR(resourceName);
			Marshal.FreeBSTR(objectName);
			Release(editor);
		}
	}
}

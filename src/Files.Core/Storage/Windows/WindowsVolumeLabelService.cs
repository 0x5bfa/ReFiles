// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Reads Shell drive display names and changes filesystem volume labels.
/// </summary>
public static unsafe class WindowsVolumeLabelService
{
	private const int ContinueButtonId = 1;
	private const int ErrorAccessDenied = 5;
	private const int HResultCanceled = unchecked((int)0x800704C7);
	private const uint TaskDialogCreatedNotification = 0;
	private const uint TaskDialogSetButtonElevationRequiredState = 0x473;
	private const ushort AccessDeniedTitleResourceId = 50178;
	private const ushort AdminPermissionResourceId = 50179;
	private const ushort ContinueInstructionResourceId = 50180;
	private const ushort ContinueResourceId = 50177;
	private const ushort ShieldIconResourceId = 65534;

	/// <summary>
	/// Gets the localized Shell display name for a drive root.
	/// </summary>
	/// <param name="rootPath">The drive root path.</param>
	/// <returns>The localized Shell display name, including the drive designator when available.</returns>
	public static string GetDisplayName(string rootPath)
	{
		var root = GetRootPath(rootPath);
		if (PInvoke.SHCreateItemFromParsingName(root, null, out IShellItem shellItem).Succeeded)
		{
			var displayName = ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_NORMALDISPLAY);
			if (!string.IsNullOrWhiteSpace(displayName))
			{
				return displayName;
			}
		}

		return Path.TrimEndingDirectorySeparator(root);
	}

	/// <summary>
	/// Changes or clears a filesystem volume label and notifies the Shell.
	/// </summary>
	/// <param name="owner">The window that owns the permission dialog and elevation prompt.</param>
	/// <param name="rootPath">The drive root path.</param>
	/// <param name="label">The new volume label, or an empty string to clear it.</param>
	public static void SetLabel(HWND owner, string rootPath, string label)
	{
		ArgumentNullException.ThrowIfNull(label);

		var root = GetRootPath(rootPath);
		if (PInvoke.SetVolumeLabel(root, label))
		{
			NotifyShell(root);

			return;
		}

		var error = Marshal.GetLastPInvokeError();
		if (error is not ErrorAccessDenied)
		{
			throw new Win32Exception(error);
		}

		if (!ShowElevationDialog(owner))
		{
			throw new OperationCanceledException("The volume-label change was canceled.");
		}

		SetLabelElevated(owner, root, label);
	}

	private static string GetRootPath(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var root = Path.GetPathRoot(path);
		if (string.IsNullOrWhiteSpace(root))
		{
			throw new ArgumentException("The path does not identify a filesystem root.", nameof(path));
		}

		return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
	}

	private static void NotifyShell(string root)
	{
		fixed (char* rootPointer = root)
		{
			PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_RENAMEFOLDER, SHCNF_FLAGS.SHCNF_PATHW, rootPointer, rootPointer);
		}
	}

	private static PCWSTR ResourcePointer(ushort resourceId)
	{
		return (char*)resourceId;
	}

	private static void SetLabelElevated(HWND owner, string root, string label)
	{
		var classId = typeof(CMountPointRename).GUID;
		var interfaceId = typeof(IMountPointRename).GUID;
		BIND_OPTS3 bindOptions = default;
		bindOptions.Base.Base.cbStruct = checked((uint)Marshal.SizeOf<BIND_OPTS3>());
		bindOptions.Base.dwClassContext = (uint)CLSCTX.CLSCTX_LOCAL_SERVER;
		bindOptions.hwnd = owner;
		var hr = PInvoke.CoGetObject($"Elevation:Administrator!new:{classId:B}", in bindOptions, in interfaceId, out object instanceObject);
		var instance = instanceObject as IMountPointRename;
		ThrowOnFailureOrCancellation(hr);
		if (instance is null)
		{
			throw new COMException("The elevated mount-point rename service did not return an interface.", HRESULT.E_NOINTERFACE);
		}

		hr = instance.Rename(root, label);
		ThrowOnFailureOrCancellation(hr);
	}

	private static bool ShowElevationDialog(HWND owner)
	{
		const string shellModuleName = "shell32.dll";
		fixed (char* moduleNamePointer = shellModuleName)
		{
			var shellModule = PInvoke.GetModuleHandle(moduleNamePointer);
			if (shellModule.IsNull)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError());
			}

			TASKDIALOG_BUTTON continueButton = default;
			continueButton.nButtonID = ContinueButtonId;
			continueButton.pszButtonText = ResourcePointer(ContinueResourceId);
			TASKDIALOGCONFIG configuration = default;
			configuration.cbSize = checked((uint)sizeof(TASKDIALOGCONFIG));
			configuration.hwndParent = owner;
			configuration.hInstance = shellModule;
			configuration.dwCommonButtons = TASKDIALOG_COMMON_BUTTON_FLAGS.TDCBF_CANCEL_BUTTON;
			configuration.pszWindowTitle = ResourcePointer(AccessDeniedTitleResourceId);
			configuration.pszMainInstruction = ResourcePointer(AdminPermissionResourceId);
			configuration.pszContent = ResourcePointer(ContinueInstructionResourceId);
			configuration.cButtons = 1;
			configuration.pButtons = &continueButton;
			configuration.pfCallback = &TaskDialogCallback;
			configuration.pszMainIcon = ResourcePointer(ShieldIconResourceId);
			var hr = PInvoke.TaskDialogIndirect(in configuration, out var selectedButton, out _, out _);
			hr.ThrowOnFailure();

			return selectedButton is ContinueButtonId;
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static HRESULT TaskDialogCallback(HWND window, uint notification, WPARAM buttonId, LPARAM value, nint callbackData)
	{
		if (notification is TaskDialogCreatedNotification)
		{
			PInvoke.SendMessage(window, TaskDialogSetButtonElevationRequiredState, (nuint)ContinueButtonId, 1);
		}

		return HRESULT.S_OK;
	}

	private static void ThrowOnFailureOrCancellation(HRESULT hr)
	{
		if (hr.Value is HResultCanceled)
		{
			throw new OperationCanceledException("The volume-label change was canceled.");
		}

		hr.ThrowOnFailure();
	}
}

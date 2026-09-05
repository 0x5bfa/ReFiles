// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

/// <summary>
/// Launches the Windows drive-maintenance tools represented by the Shell Tools property page.
/// </summary>
public static unsafe class WindowsShellDriveToolsService
{
	private const uint SeeMaskFlagLogUsage = 0x04000000;
	private const int ErrorCancelled = 1223;

	/// <summary>
	/// Starts an elevated online filesystem check for a volume.
	/// </summary>
	/// <param name="owner">The window that owns the elevation prompt.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <returns>The HRESULT representing the launch result.</returns>
	public static HRESULT ShowErrorChecking(HWND owner, string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		var volume = NormalizeVolumeArgument(rootPath);

		return Execute(owner, "runas", Path.Combine(Environment.SystemDirectory, "chkdsk.exe"), QuoteArgument(volume));
	}

	/// <summary>
	/// Opens the Windows drive optimization application for a volume.
	/// </summary>
	/// <param name="owner">The window that owns any launch UI.</param>
	/// <param name="rootPath">The volume root path.</param>
	/// <returns>The HRESULT representing the launch result.</returns>
	public static HRESULT ShowOptimization(HWND owner, string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		var volume = NormalizeVolumeArgument(rootPath);

		return Execute(owner, "open", Path.Combine(Environment.SystemDirectory, "dfrgui.exe"), QuoteArgument(volume));
	}

	private static HRESULT Execute(HWND owner, string verb, string fileName, string parameters)
	{
		fixed (char* verbPointer = verb)
		fixed (char* fileNamePointer = fileName)
		fixed (char* parametersPointer = parameters)
		{
			var executeInfo = new SHELLEXECUTEINFOW
			{
				cbSize = checked((uint)sizeof(SHELLEXECUTEINFOW)),
				fMask = SeeMaskFlagLogUsage,
				hwnd = owner,
				lpVerb = new PCWSTR(verbPointer),
				lpFile = new PCWSTR(fileNamePointer),
				lpParameters = new PCWSTR(parametersPointer),
				nShow = 1,
			};
			if (PInvoke.ShellExecuteEx(ref executeInfo))
			{
				return HRESULT.S_OK;
			}

			var error = Marshal.GetLastPInvokeError();
			if (error is ErrorCancelled)
			{
				return HRESULT.S_FALSE;
			}

			return (HRESULT)unchecked((int)(0x80070000u | ((uint)error & 0xFFFFu)));
		}
	}

	private static string NormalizeVolumeArgument(string rootPath)
	{
		var root = Path.GetPathRoot(rootPath) ?? rootPath;
		if (root.Length is 3 && root[1] is ':' && Path.EndsInDirectorySeparator(root))
		{
			return root[..2];
		}

		return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static string QuoteArgument(string argument)
	{
		return $"\"{argument.Replace("\"", "\\\"")}\"";
	}
}

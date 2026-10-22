// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace FilesLauncher;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		PInvoke.OleInitialize().ThrowOnFailure();

		try
		{
			Run(args);
		}
		finally
		{
			PInvoke.OleUninitialize();
		}
	}

	private static void Run(string[] args)
	{
		var launchPath = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WindowsApps\files-dev.exe");
		var openDirectory = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : null;

		if (!File.Exists(launchPath))
		{
			PInvoke.MessageBox(HWND.Null, "Files has been uninstalled. Restoring File Explorer.", "Files", MESSAGEBOX_STYLE.MB_OK);
			RestoreFileExplorerDefaults();
			RunFileExplorer(openDirectory);

			return;
		}

		if (openDirectory is not null && ShellWindowNavigation.OpenInExistingShellWindow(openDirectory))
		{
			return;
		}

		if (openDirectory is null)
		{
			ExecuteShellItem(null, "files-dev:", null, null, SHOW_WINDOW_CMD.SW_SHOW, PInvoke.SEE_MASK_NOASYNC | PInvoke.SEE_MASK_FLAG_NO_UI);

			return;
		}

		var selectedItem = OpenInFolder.Run(openDirectory);
		var commandLine = selectedItem is null
			? $"\"{launchPath}\" -directory \"{openDirectory}\""
			: $"\"{launchPath}\" -select \"{selectedItem}\"";
		var protocolUri = $"files-dev:?cmd={EncodeUtf8AsPercentHex(commandLine)}";
		ExecuteShellItem(null, protocolUri, null, openDirectory, SHOW_WINDOW_CMD.SW_SHOW, PInvoke.SEE_MASK_NOASYNC | PInvoke.SEE_MASK_FLAG_NO_UI);
	}

	internal static void RunFileExplorer(string? openDirectory)
	{
		var arguments = openDirectory is null ? null : $"\"{openDirectory}\"";
		ExecuteShellItem(null, "explorer.exe", arguments, null, SHOW_WINDOW_CMD.SW_SHOW, 0);
	}

	private static void RestoreFileExplorerDefaults()
	{
		var importArguments = @"/c reg.exe import ""%LocalAppData%\Files\UnsetFilesAsDefault.reg""";
		if (!TryShellExecute("runas", "cmd.exe", importArguments, null, SHOW_WINDOW_CMD.SW_HIDE))
		{
			return;
		}

		var cleanupArguments = @"-command ""Start-Sleep -Seconds 5; $lfp = [System.Environment]::ExpandEnvironmentVariables('%%LocalAppData%%\Files'); Remove-Item -Path $lfp -Recurse -Force""";
		TryShellExecute(null, "powershell.exe", cleanupArguments, null, SHOW_WINDOW_CMD.SW_HIDE);
	}

	private static bool TryShellExecute(string? verb, string file, string? parameters, string? directory, SHOW_WINDOW_CMD showCommand)
	{
		return ExecuteShellItem(verb, file, parameters, directory, showCommand, 0);
	}

	private static unsafe bool ExecuteShellItem(string? verb, string file, string? parameters, string? directory, SHOW_WINDOW_CMD showCommand, uint mask)
	{
		fixed (char* verbPointer = verb)
		fixed (char* filePointer = file)
		fixed (char* parametersPointer = parameters)
		fixed (char* directoryPointer = directory)
		{
			var executeInfo = default(SHELLEXECUTEINFOW);
			executeInfo.cbSize = (uint)sizeof(SHELLEXECUTEINFOW);
			executeInfo.fMask = mask;
			executeInfo.lpVerb = verbPointer;
			executeInfo.lpFile = filePointer;
			executeInfo.lpParameters = parametersPointer;
			executeInfo.lpDirectory = directoryPointer;
			executeInfo.nShow = (int)showCommand;

			return PInvoke.ShellExecuteEx(ref executeInfo);
		}
	}

	private static string EncodeUtf8AsPercentHex(string value)
	{
		var builder = new StringBuilder();
		foreach (var valueByte in Encoding.UTF8.GetBytes(value))
		{
			builder.Append('%').Append(valueByte.ToString("X2", CultureInfo.InvariantCulture));
		}

		return builder.ToString();
	}
}

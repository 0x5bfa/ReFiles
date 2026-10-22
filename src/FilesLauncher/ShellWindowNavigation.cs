// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace FilesLauncher;

internal static unsafe class ShellWindowNavigation
{
	private const string GodModeClsid = "{ED7BA470-8E54-465E-825C-99712043E01C}";
	private const string ControlPanelCategoryParsingName = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
	private static readonly string[] _supportedShellFolders =
	[
		"shell:::{645FF040-5081-101B-9F08-00AA002F954E}",
		"shell:::{5E5F29CE-E0A8-49D3-AF32-7A7BDC173478}",
		"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
		"shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
		"shell:::{208D2C60-3AEA-1069-A2D7-08002B30309D}",
		"Shell:RecycleBinFolder",
		"Shell:NetworkPlacesFolder",
		"Shell:MyComputerFolder",
	];

	internal static bool OpenInExistingShellWindow(string folderPath)
	{
		var openDirectory = folderPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ? $"shell:{folderPath}" : folderPath;
		var mustOpenInExplorer = openDirectory.Contains(GodModeClsid, StringComparison.OrdinalIgnoreCase);

		if (openDirectory.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
		{
			mustOpenInExplorer = !_supportedShellFolders.Contains(openDirectory, StringComparer.OrdinalIgnoreCase);
		}

		if (!TryCreatePidl(ControlPanelCategoryParsingName, out var controlPanelCategoryPidl))
		{
			OpenInExplorerIfRequired(openDirectory, mustOpenInExplorer);

			return mustOpenInExplorer;
		}

		if (!TryCreatePidl(openDirectory, out var targetFolderPidl))
		{
			PInvoke.CoTaskMemFree(controlPanelCategoryPidl);
			OpenInExplorerIfRequired(openDirectory, mustOpenInExplorer);

			return mustOpenInExplorer;
		}

		try
		{
			return TryNavigateExistingWindow(controlPanelCategoryPidl, targetFolderPidl) || OpenInExplorerIfRequired(openDirectory, mustOpenInExplorer);
		}
		finally
		{
			PInvoke.CoTaskMemFree(targetFolderPidl);
			PInvoke.CoTaskMemFree(controlPanelCategoryPidl);
		}
	}

	private static bool TryNavigateExistingWindow(ITEMIDLIST* controlPanelCategoryPidl, ITEMIDLIST* targetFolderPidl)
	{
		if (PInvoke.CoCreateInstance(typeof(ShellWindowsCoclass).GUID, null, CLSCTX.CLSCTX_LOCAL_SERVER, out IShellWindows? shellWindows).Failed
			|| shellWindows is null
			|| shellWindows.get_Count(out var windowCount).Failed)
		{
			return false;
		}

		for (var index = 0; index < windowCount; index++)
		{
			using var indexVariant = ComVariant.Create(index);
			if (shellWindows.Item(indexVariant, out var item).Failed || item is null)
			{
				continue;
			}

			var serviceProvider = item as Windows.Win32.System.Com.IServiceProvider;
			if (serviceProvider is null || serviceProvider.QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser? shellBrowser).Failed || shellBrowser is null)
			{
				continue;
			}

			if (shellBrowser.QueryActiveShellView(out var activeShellView).Failed)
			{
				continue;
			}

			var folderView = activeShellView as IFolderView;
			if (folderView is null || folderView.GetFolder<IPersistFolder2>(out var folder).Failed || folder is null || folder.GetCurFolder(out var currentFolderPidl).Failed || currentFolderPidl is null)
			{
				continue;
			}

			try
			{
				ref readonly var currentFolder = ref *currentFolderPidl;
				ref readonly var targetFolder = ref *targetFolderPidl;
				ref readonly var controlPanelCategory = ref *controlPanelCategoryPidl;
				if ((PInvoke.ILIsParent(in currentFolder, in targetFolder, true).Value != 0 || PInvoke.ILIsEqual(in currentFolder, in controlPanelCategory).Value != 0)
					&& shellBrowser.BrowseObject(in targetFolder, PInvoke.SBSP_SAMEBROWSER | PInvoke.SBSP_ABSOLUTE).Succeeded)
				{
					return true;
				}
			}
			finally
			{
				PInvoke.CoTaskMemFree(currentFolderPidl);
			}
		}

		return false;
	}

	private static bool TryCreatePidl(string parsingName, out ITEMIDLIST* pidl)
	{
		pidl = null;
		if (PInvoke.SHCreateItemFromParsingName(parsingName, null, out IShellItem? shellItem).Failed || shellItem is null)
		{
			return false;
		}

		return PInvoke.SHGetIDListFromObject(shellItem, out pidl).Succeeded && pidl is not null;
	}

	private static bool OpenInExplorerIfRequired(string openDirectory, bool mustOpenInExplorer)
	{
		if (mustOpenInExplorer)
		{
			Program.RunFileExplorer(openDirectory);
		}

		return mustOpenInExplorer;
	}

	[Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
	internal sealed class ShellWindowsCoclass
	{
	}
}

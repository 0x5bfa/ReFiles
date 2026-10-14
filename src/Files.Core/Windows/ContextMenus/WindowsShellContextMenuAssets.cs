// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.Core.Windows;

internal static class WindowsShellContextMenuAssets
{
	private const string PackageFamilyName = "MicrosoftWindows.Client.FileExp_cw5n1h2txyewy";

	internal static ReadOnlyMemory<byte> Read(string canonicalVerb, WindowsShellContextMenuIconTheme theme)
	{
		// FileExplorerExtensions::GetIconFilenameForContextMenuVerb uses these system SVG assets.
		var filename = canonicalVerb switch
		{
			"MakeAvailableOffline" => "MakeAvailableOffline.svg",
			"MakeAvailableOnline" => "MakeAvailableOnline.svg",
			_ => null,
		};
		if (filename is null || GetPackagePath() is not { } packagePath)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		var themePath = theme switch
		{
			WindowsShellContextMenuIconTheme.Dark => Path.Combine("contrast-standard", "theme-dark"),
			WindowsShellContextMenuIconTheme.HighContrastWhite => "contrast-white",
			WindowsShellContextMenuIconTheme.HighContrastBlack => "contrast-black",
			_ => Path.Combine("contrast-standard", "theme-light"),
		};
		try
		{
			return File.ReadAllBytes(Path.Combine(packagePath, "FileExplorerExtensions", "Assets", "images", themePath, filename));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return ReadOnlyMemory<byte>.Empty;
		}
	}

	private static unsafe string? GetPackagePath()
	{
		uint count = 0;
		uint bufferLength = 0;
		var result = PInvoke.GetPackagesByPackageFamily(PackageFamilyName, ref count, [], ref bufferLength, []);
		if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || count is 0 || bufferLength is 0)
		{
			return null;
		}

		var names = new PWSTR[checked((int)count)];
		var buffer = new char[checked((int)bufferLength)];
		fixed (char* characters = buffer)
		{
			result = PInvoke.GetPackagesByPackageFamily(PackageFamilyName, ref count, names, ref bufferLength, new Span<char>(characters, buffer.Length));

			return result == WIN32_ERROR.ERROR_SUCCESS && count is not 0 ? WindowsShellIconProvider.GetPackagePath(names[0].ToString()) : null;
		}
	}
}

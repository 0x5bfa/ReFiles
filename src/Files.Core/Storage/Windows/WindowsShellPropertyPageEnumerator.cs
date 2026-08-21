// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Windows;

internal static class WindowsShellPropertyPageEnumerator
{
	private static readonly HashSet<string> _signatureExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".appx",
		".cat",
		".cpl",
		".dll",
		".exe",
		".lnk",
		".msi",
		".msix",
		".ocx",
		".scr",
		".sys",
	};

	internal static IReadOnlyList<WindowsShellPropertyPage> GetPages(WindowsShellResolvedSelection selection)
	{
		ArgumentNullException.ThrowIfNull(selection);

		var pages = new List<WindowsShellPropertyPage>
		{
			new(WindowsShellPropertyPageKind.General, string.Empty, true),
		};
		var primaryPath = selection.FileSystemPaths.Count is 1 ? selection.FileSystemPaths[0] : null;
		var isShortcut = primaryPath is not null && Path.GetExtension(primaryPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
		if (isShortcut)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Shortcut, string.Empty, false));
		}

		if (selection.IsSingleFolder)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Sharing, string.Empty, false));
			pages.Add(new(WindowsShellPropertyPageKind.Security, string.Empty, false));
			pages.Add(new(WindowsShellPropertyPageKind.PreviousVersions, string.Empty, false));
			pages.Add(new(WindowsShellPropertyPageKind.Customize, string.Empty, false));

			return pages;
		}

		if (primaryPath is not null && _signatureExtensions.Contains(Path.GetExtension(primaryPath)))
		{
			pages.Add(new(WindowsShellPropertyPageKind.DigitalSignatures, string.Empty, false));
		}

		if (primaryPath is not null)
		{
			pages.Add(new(WindowsShellPropertyPageKind.Security, string.Empty, false));
		}

		pages.Add(new(WindowsShellPropertyPageKind.Details, string.Empty, false));
		if (primaryPath is not null)
		{
			pages.Add(new(WindowsShellPropertyPageKind.PreviousVersions, string.Empty, false));
		}

		return pages;
	}
}

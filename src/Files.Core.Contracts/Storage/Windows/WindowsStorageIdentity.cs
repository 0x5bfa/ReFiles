// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Identifies addresses supported by the built-in Windows source.</summary>
public static class WindowsStorageIdentity
{
	/// <summary>The built-in source type.</summary>
	public const string DefaultSourceType = "windows-shell";
	/// <summary>The filesystem address scheme.</summary>
	public const string FileAddressScheme = "file";
	/// <summary>The virtual Shell address scheme.</summary>
	public const string ShellAddressScheme = "shell";
	/// <summary>The Computer namespace root's parsing name.</summary>
	public const string ComputerParsingName = "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}";
}

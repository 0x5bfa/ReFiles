// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Classifies Windows Shell parsing names for special locations.</summary>
public static class WindowsShellLocation
{
	private const string WslNamespaceClassId = "{B2B4A4D1-2754-4140-A2EB-9A76D9D7CDC6}";

	private const string WslLocalhostRoot = @"\\wsl.localhost";

	private const string WslLegacyRoot = @"\\wsl$";

	/// <summary>Determines whether a parsing name identifies a WSL location.</summary>
	/// <param name="parsingName">The Windows Shell parsing name.</param>
	/// <returns><see langword="true"/> when the parsing name identifies WSL; otherwise, <see langword="false"/>.</returns>
	public static bool IsWsl(string parsingName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		return parsingName.StartsWith($"::{WslNamespaceClassId}", StringComparison.OrdinalIgnoreCase)
			|| parsingName.StartsWith($"shell:::{WslNamespaceClassId}", StringComparison.OrdinalIgnoreCase)
			|| IsWithinRoot(parsingName, WslLocalhostRoot)
			|| IsWithinRoot(parsingName, WslLegacyRoot);
	}

	private static bool IsWithinRoot(string parsingName, string root)
	{
		return parsingName.Equals(root, StringComparison.OrdinalIgnoreCase) || parsingName.StartsWith($"{root}\\", StringComparison.OrdinalIgnoreCase);
	}
}

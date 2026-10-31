// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.UITests;

/// <summary>Starts the WinUI test host in an STA.</summary>
internal static class Program
{
	[STAThread]
	private static void Main()
	{
		XamlGeneratedProgram.XamlGeneratedMain();
	}
}

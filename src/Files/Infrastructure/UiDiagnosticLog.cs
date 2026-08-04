// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;

namespace Files.Infrastructure;

internal static class UiDiagnosticLog
{
	private static readonly bool _isEnabled = string.Equals(Environment.GetEnvironmentVariable("FILES_DIAGNOSTIC_LOG"), "1", StringComparison.Ordinal);

	[Conditional("DEBUG")]
	internal static void Write(string component, string message)
	{
		if (!_isEnabled)
		{
			return;
		}

		Debug.WriteLine($"[Files.UI] timestamp={Stopwatch.GetTimestamp()} thread={Environment.CurrentManagedThreadId} {component}: {message}");
	}
}

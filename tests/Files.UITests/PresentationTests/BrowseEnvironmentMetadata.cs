// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;

namespace Files.UITests;

internal sealed class BrowseEnvironmentMetadata
{
	public required string OsVersion { get; init; }
	public required string ProcessArchitecture { get; init; }
	public string? Processor { get; init; }
	public long AvailableMemoryBytes { get; init; }
	public string? Notes { get; init; }

	public static BrowseEnvironmentMetadata Create(string? notes)
	{
		return new()
		{
			OsVersion = Environment.OSVersion.VersionString,
			ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
			Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
			AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
			Notes = notes,
		};
	}
}

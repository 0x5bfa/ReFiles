// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains capabilities used by the drive General and Tools pages.
/// </summary>
public sealed class WindowsShellDriveProperties
{
	/// <summary>Gets the normalized volume-root path.</summary>
	public string RootPath { get; }

	/// <summary>Gets the value returned by <c>GetDriveTypeW</c>.</summary>
	public uint DriveType { get; }

	/// <summary>Gets the filesystem capability flags returned by <c>GetVolumeInformationW</c>.</summary>
	public uint FileSystemFlags { get; }

	/// <summary>Gets a value indicating whether Explorer exposes its error-checking command.</summary>
	public bool SupportsErrorChecking { get; }

	/// <summary>Gets a value indicating whether Explorer exposes its optimization command.</summary>
	public bool SupportsOptimization { get; }

	internal WindowsShellDriveProperties(string rootPath, uint driveType, uint fileSystemFlags, bool supportsErrorChecking, bool supportsOptimization)
	{
		RootPath = rootPath;
		DriveType = driveType;
		FileSystemFlags = fileSystemFlags;
		SupportsErrorChecking = supportsErrorChecking;
		SupportsOptimization = supportsOptimization;
	}
}

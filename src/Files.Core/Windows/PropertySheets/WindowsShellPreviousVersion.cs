// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes one File History or volume snapshot version.
/// </summary>
public sealed class WindowsShellPreviousVersion
{
	/// <summary>Gets the version's display name.</summary>
	public string Name { get; }

	/// <summary>Gets the version's source path.</summary>
	public string SourcePath { get; }

	/// <summary>Gets the version timestamp.</summary>
	public DateTimeOffset DateModified { get; }

	internal WindowsShellPreviousVersion(string name, string sourcePath, DateTimeOffset dateModified)
	{
		Name = name;
		SourcePath = sourcePath;
		DateModified = dateModified;
	}
}

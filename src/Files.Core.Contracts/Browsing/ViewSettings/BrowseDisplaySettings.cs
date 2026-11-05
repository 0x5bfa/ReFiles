// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>
/// Contains application-wide browse display preferences.
/// </summary>
public sealed record BrowseDisplaySettings
{
	/// <summary>Gets the default browse display settings.</summary>
	public static BrowseDisplaySettings Default { get; } = new();

	/// <summary>Gets a value indicating whether hidden items are shown.</summary>
	public bool ShowHiddenItems { get; }

	/// <summary>Gets a value indicating whether file extensions are shown.</summary>
	public bool ShowFileExtensions { get; }

	/// <summary>Initializes application-wide browse display preferences.</summary>
	/// <param name="showHiddenItems">Whether hidden items are shown.</param>
	/// <param name="showFileExtensions">Whether file extensions are shown.</param>
	public BrowseDisplaySettings(bool showHiddenItems = false, bool showFileExtensions = true)
	{
		ShowHiddenItems = showHiddenItems;
		ShowFileExtensions = showFileExtensions;
	}
}

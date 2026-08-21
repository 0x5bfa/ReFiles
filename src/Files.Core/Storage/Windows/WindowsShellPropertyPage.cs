// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Describes a property page that the Windows Shell makes available for a selection.
/// </summary>
public sealed class WindowsShellPropertyPage
{
	/// <summary>
	/// Gets the title displayed by the native property page.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets a value indicating whether the page was contributed by the Shell's default property page provider.
	/// </summary>
	public bool IsDefault { get; }

	/// <summary>
	/// Initializes a Windows Shell property page description.
	/// </summary>
	/// <param name="title">The native property page title.</param>
	/// <param name="isDefault">Whether the page came from the default property page provider.</param>
	public WindowsShellPropertyPage(string title, bool isDefault)
	{
		ArgumentNullException.ThrowIfNull(title);

		Title = title;
		IsDefault = isDefault;
	}
}

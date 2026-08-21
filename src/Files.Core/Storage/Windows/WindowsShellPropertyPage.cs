// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Describes a Windows-style property page that ReFiles can render for a selection.
/// </summary>
public sealed class WindowsShellPropertyPage
{
	/// <summary>
	/// Gets the ReFiles property-page implementation to use.
	/// </summary>
	public WindowsShellPropertyPageKind Kind { get; }

	/// <summary>
	/// Gets the title override supplied for the page.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets a value indicating whether this is the selection's default page.
	/// </summary>
	public bool IsDefault { get; }

	/// <summary>
	/// Initializes a Windows Shell property page description.
	/// </summary>
	/// <param name="kind">The property-page implementation.</param>
	/// <param name="title">The native property page title, or an empty string to use the localized ReFiles title.</param>
	/// <param name="isDefault">Whether this is the selection's default page.</param>
	public WindowsShellPropertyPage(WindowsShellPropertyPageKind kind, string title, bool isDefault)
	{
		ArgumentNullException.ThrowIfNull(title);

		Kind = kind;
		Title = title;
		IsDefault = isDefault;
	}
}

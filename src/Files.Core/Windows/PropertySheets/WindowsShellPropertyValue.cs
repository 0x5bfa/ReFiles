// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains one formatted value from a Shell property-description list.
/// </summary>
public sealed class WindowsShellPropertyValue
{
	/// <summary>Gets the localized property display name.</summary>
	public string Name { get; }

	/// <summary>Gets the property value formatted by its property description.</summary>
	public string Value { get; }

	/// <summary>Gets a value indicating whether this entry is a property group heading.</summary>
	public bool IsGroup { get; }

	internal WindowsShellPropertyValue(string name, string value, bool isGroup)
	{
		Name = name;
		Value = value;
		IsGroup = isGroup;
	}
}

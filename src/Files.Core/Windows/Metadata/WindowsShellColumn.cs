// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Describes one column exposed by a Windows Shell folder.
/// </summary>
public sealed record WindowsShellColumn
{
	/// <summary>Gets the zero-based column index used by the Shell folder.</summary>
	public int Index { get; }

	/// <summary>Gets the stable property identifier used by Files settings and property requests.</summary>
	public string PropertyId { get; }

	/// <summary>Gets the localized column heading returned by the Shell folder.</summary>
	public string DisplayName { get; }

	/// <summary>Gets the heading width suggested by the Shell, measured in average-sized characters.</summary>
	public int HeaderWidthCharacters { get; }

	/// <summary>Gets the value alignment suggested by the Shell.</summary>
	public WindowsShellColumnAlignment Alignment { get; }

	/// <summary>Gets the display type suggested by the Shell.</summary>
	public WindowsShellColumnType Type { get; }

	/// <summary>Gets a value indicating whether the column is enabled in the Shell's default Details view.</summary>
	public bool IsVisibleByDefault { get; }

	/// <summary>Gets a value indicating whether the column is marked as hidden by the Shell.</summary>
	public bool IsHidden { get; }

	/// <summary>Gets a value indicating whether retrieving values can be slow.</summary>
	public bool IsSlow { get; }

	/// <summary>Gets a value indicating whether the column is supplied by a property handler.</summary>
	public bool IsExtended { get; }

	/// <summary>Gets a value indicating whether the column is intended for secondary property UI.</summary>
	public bool IsSecondaryUi { get; }

	/// <summary>Gets a value indicating whether grouping is supported for the column.</summary>
	public bool CanGroup { get; }

	/// <summary>Gets a value indicating whether the Shell fixes the column width.</summary>
	public bool IsFixedWidth { get; }

	/// <summary>Gets a value indicating whether variant comparison is preferred for sorting.</summary>
	public bool PreferVariantCompare { get; }

	/// <summary>Initializes a Shell column description.</summary>
	/// <param name="index">The zero-based Shell column index.</param>
	/// <param name="propertyId">The stable property identifier.</param>
	/// <param name="displayName">The localized display name.</param>
	/// <param name="headerWidthCharacters">The suggested header width in average-sized characters.</param>
	/// <param name="alignment">The suggested value alignment.</param>
	/// <param name="isVisibleByDefault">Whether the Shell enables the column by default.</param>
	/// <param name="isHidden">Whether the Shell marks the column as hidden.</param>
	/// <param name="isSlow">Whether retrieving values can be slow.</param>
	/// <param name="isExtended">Whether a property handler supplies the column.</param>
	/// <param name="isSecondaryUi">Whether the column is intended for secondary property UI.</param>
	/// <param name="canGroup">Whether grouping is supported.</param>
	/// <param name="isFixedWidth">Whether the Shell fixes the column width.</param>
	/// <param name="preferVariantCompare">Whether variant comparison is preferred for sorting.</param>
	/// <param name="type">The display type suggested by the Shell.</param>
	public WindowsShellColumn(
		int index,
		string propertyId,
		string displayName,
		int headerWidthCharacters,
		WindowsShellColumnAlignment alignment,
		bool isVisibleByDefault,
		bool isHidden,
		bool isSlow,
		bool isExtended,
		bool isSecondaryUi,
		bool canGroup,
		bool isFixedWidth,
		bool preferVariantCompare,
		WindowsShellColumnType type = WindowsShellColumnType.Default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		ArgumentOutOfRangeException.ThrowIfNegative(headerWidthCharacters);

		Index = index;
		PropertyId = propertyId;
		DisplayName = displayName;
		HeaderWidthCharacters = headerWidthCharacters;
		Alignment = alignment;
		Type = type;
		IsVisibleByDefault = isVisibleByDefault;
		IsHidden = isHidden;
		IsSlow = isSlow;
		IsExtended = isExtended;
		IsSecondaryUi = isSecondaryUi;
		CanGroup = canGroup;
		IsFixedWidth = isFixedWidth;
		PreferVariantCompare = preferVariantCompare;
	}
}

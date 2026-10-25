// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Browsing;

/// <summary>Describes a provider-independent property column.</summary>
public sealed record BrowseColumn
{
	/// <summary>Gets the provider column order.</summary>
	public int Index { get; }
	/// <summary>Gets the stable property identifier.</summary>
	public string PropertyId { get; }
	/// <summary>Gets the provider-supplied heading.</summary>
	public string DisplayName { get; }
	/// <summary>Gets the suggested width in device-independent pixels.</summary>
	public double DefaultWidth { get; }
	/// <summary>Gets the value alignment.</summary>
	public BrowseColumnAlignment Alignment { get; }
	/// <summary>Gets whether the column is visible by default.</summary>
	public bool IsVisibleByDefault { get; }
	/// <summary>Gets whether the column is hidden.</summary>
	public bool IsHidden { get; }
	/// <summary>Gets whether the column can be sorted.</summary>
	public bool CanSort { get; }
	/// <summary>Gets whether the column can be grouped.</summary>
	public bool CanGroup { get; }
	/// <summary>Gets whether the column has a fixed width.</summary>
	public bool IsFixedWidth { get; }

	/// <summary>Initializes a property column.</summary>
	/// <param name="index">The provider column order.</param>
	/// <param name="propertyId">The stable property identifier.</param>
	/// <param name="displayName">The heading.</param>
	/// <param name="defaultWidth">The suggested width.</param>
	/// <param name="alignment">The value alignment.</param>
	/// <param name="isVisibleByDefault">Whether the column is visible by default.</param>
	/// <param name="isHidden">Whether the column is hidden.</param>
	/// <param name="canSort">Whether sorting is supported.</param>
	/// <param name="canGroup">Whether grouping is supported.</param>
	/// <param name="isFixedWidth">Whether the width is fixed.</param>
	public BrowseColumn(int index, string propertyId, string displayName, double defaultWidth = 120, BrowseColumnAlignment alignment = BrowseColumnAlignment.Left,
		bool isVisibleByDefault = true, bool isHidden = false, bool canSort = true, bool canGroup = true, bool isFixedWidth = false)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		if (!double.IsFinite(defaultWidth) || defaultWidth <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(defaultWidth));
		}

		Index = index;
		PropertyId = propertyId;
		DisplayName = displayName;
		DefaultWidth = defaultWidth;
		Alignment = alignment;
		IsVisibleByDefault = isVisibleByDefault;
		IsHidden = isHidden;
		CanSort = canSort;
		CanGroup = canGroup;
		IsFixedWidth = isFixedWidth;
	}
}

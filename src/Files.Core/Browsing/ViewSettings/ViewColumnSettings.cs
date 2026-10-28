// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Describes the persisted layout of one view column.</summary>
public sealed record ViewColumnSettings
{
	/// <summary>Gets the property identifier displayed by the column.</summary>
	public string PropertyId { get; }

	/// <summary>Gets the column width in device-independent pixels.</summary>
	public double Width { get; }

	/// <summary>Gets the zero-based column order.</summary>
	public int Order { get; }

	/// <summary>Gets a value indicating whether the column is visible.</summary>
	public bool IsVisible { get; }

	/// <summary>Initializes column settings.</summary>
	/// <param name="propertyId">The property identifier displayed by the column.</param>
	/// <param name="width">The column width in device-independent pixels.</param>
	/// <param name="order">The zero-based column order.</param>
	/// <param name="isVisible">Whether the column is visible.</param>
	public ViewColumnSettings(string propertyId, double width, int order, bool isVisible = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		if (!double.IsFinite(width) || width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(order);

		PropertyId = propertyId;
		Width = width;
		Order = order;
		IsVisible = isVisible;
	}
}

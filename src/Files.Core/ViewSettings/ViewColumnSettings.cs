// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

public sealed record ViewColumnSettings
{
	public string PropertyId { get; }

	public double Width { get; }

	public int Order { get; }

	public bool IsVisible { get; }

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

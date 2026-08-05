// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage.Windows;

namespace Files.ViewModels;

/// <summary>
/// Describes one column rendered by the details folder view.
/// </summary>
public sealed class DetailsColumnViewModel
{
	/// <summary>Gets the stable property identifier.</summary>
	public string PropertyId { get; }

	/// <summary>Gets the localized display name supplied by the storage source.</summary>
	public string DisplayName { get; }

	/// <summary>Gets the column width in device-independent pixels.</summary>
	public double Width { get; }

	/// <summary>Gets the value alignment suggested by the storage source.</summary>
	public WindowsShellColumnAlignment Alignment { get; }

	/// <summary>Gets a value indicating whether the column consumes remaining row width.</summary>
	public bool IsStretch { get; }

	/// <summary>
	/// Initializes a details view column.
	/// </summary>
	/// <param name="propertyId">The stable property identifier.</param>
	/// <param name="displayName">The localized display name.</param>
	/// <param name="width">The column width in device-independent pixels.</param>
	/// <param name="alignment">The value alignment.</param>
	/// <param name="isStretch">Whether the column consumes remaining row width.</param>
	public DetailsColumnViewModel(string propertyId, string displayName, double width, WindowsShellColumnAlignment alignment, bool isStretch = false)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		if (!double.IsFinite(width) || width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		PropertyId = propertyId;
		DisplayName = displayName;
		Width = width;
		Alignment = alignment;
		IsStretch = isStretch;
	}
}

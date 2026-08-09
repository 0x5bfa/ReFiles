// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Storage.Windows;
using Microsoft.UI.Xaml;

namespace Files.ViewModels;

/// <summary>
/// Describes one column rendered by the details folder view.
/// </summary>
public sealed partial class DetailsColumnViewModel : ObservableObject
{
	/// <summary>Gets the stable property identifier.</summary>
	public string PropertyId { get; }

	/// <summary>Gets the localized display name supplied by the storage source.</summary>
	public string DisplayName { get; }

	/// <summary>Gets the column width in device-independent pixels.</summary>
	[ObservableProperty]
	public partial double Width { get; set; }

	/// <summary>Gets the value alignment suggested by the storage source.</summary>
	public WindowsShellColumnAlignment Alignment { get; }

	/// <summary>Gets a value indicating whether this is the primary display column.</summary>
	public bool IsPrimary { get; }

	/// <summary>Gets a value indicating whether the user can resize the column.</summary>
	public bool CanResize { get; }

	/// <summary>Gets a value indicating whether the user can sort by the column.</summary>
	public bool CanSort { get; }

	/// <summary>Gets a value indicating whether the user can group by the column.</summary>
	public bool CanGroup { get; }

	/// <summary>Gets the minimum column width.</summary>
	public double MinWidth => 48;

	/// <summary>Gets the maximum column width.</summary>
	public double MaxWidth => 1200;

	/// <summary>Gets the text alignment used by the table.</summary>
	public TextAlignment TextAlignment => Alignment switch
	{
		WindowsShellColumnAlignment.Right => Microsoft.UI.Xaml.TextAlignment.Right,
		WindowsShellColumnAlignment.Center => Microsoft.UI.Xaml.TextAlignment.Center,
		_ => Microsoft.UI.Xaml.TextAlignment.Left,
	};

	/// <summary>
	/// Initializes a details view column.
	/// </summary>
	/// <param name="propertyId">The stable property identifier.</param>
	/// <param name="displayName">The localized display name.</param>
	/// <param name="width">The column width in device-independent pixels.</param>
	/// <param name="alignment">The value alignment.</param>
	/// <param name="isPrimary">Whether this is the primary column.</param>
	/// <param name="canResize">Whether the user can resize the column.</param>
	/// <param name="canSort">Whether the user can sort by the column.</param>
	/// <param name="canGroup">Whether the user can group by the column.</param>
	public DetailsColumnViewModel(
		string propertyId,
		string displayName,
		double width,
		WindowsShellColumnAlignment alignment,
		bool isPrimary = false,
		bool canResize = true,
		bool canSort = true,
		bool canGroup = true)
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
		IsPrimary = isPrimary;
		CanResize = canResize;
		CanSort = canSort;
		CanGroup = canGroup;
	}

	partial void OnWidthChanging(double value)
	{
		if (!double.IsFinite(value) || value <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(value));
		}

	}
}

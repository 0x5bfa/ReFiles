// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Thumbnails;

/// <summary>Describes the requested thumbnail size, density, and selection mode.</summary>
public sealed record ThumbnailRequest
{
	private const int DefaultDpi = 96;
	private const int MaximumPixelSize = 4096;

	/// <summary>
	/// Gets the requested size in logical device-independent pixels.
	/// </summary>
	public int RequestedSize { get; }

	/// <summary>Gets the thumbnail selection mode.</summary>
	public ThumbnailMode Mode { get; }

	/// <summary>
	/// Gets the display density used to convert the logical size to pixels.
	/// </summary>
	public int Dpi { get; }

	/// <summary>
	/// Gets the requested bitmap edge in physical pixels.
	/// </summary>
	public int RequestedPixelSize => (int)CalculatePixelSize(RequestedSize, Dpi);

	/// <summary>Initializes a thumbnail request.</summary>
	/// <param name="requestedSize">The requested logical bitmap edge.</param>
	/// <param name="mode">The thumbnail selection mode.</param>
	/// <param name="dpi">The display density used for pixel conversion.</param>
	public ThumbnailRequest(int requestedSize, ThumbnailMode mode = ThumbnailMode.PreferContent, int dpi = DefaultDpi)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedSize);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

		if (mode is not ThumbnailMode.Icon and not ThumbnailMode.Content and not ThumbnailMode.PreferContent)
		{
			throw new ArgumentOutOfRangeException(nameof(mode));
		}

		var pixelSize = CalculatePixelSize(requestedSize, dpi);
		if (pixelSize > MaximumPixelSize)
		{
			throw new ArgumentOutOfRangeException(nameof(requestedSize), $"The DPI-scaled thumbnail size cannot exceed {MaximumPixelSize} pixels.");
		}

		RequestedSize = requestedSize;
		Mode = mode;
		Dpi = dpi;
	}

	private static long CalculatePixelSize(int requestedSize, int dpi)
		=> ((long)requestedSize * dpi + (DefaultDpi / 2)) / DefaultDpi;
}

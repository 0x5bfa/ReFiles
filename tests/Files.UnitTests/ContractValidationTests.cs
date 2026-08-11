// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;
using Files.Core.ItemFeatures.Properties;
using Files.Core.ItemFeatures.Thumbnails;
using Files.Core.ViewSettings;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for contract validation behavior.
/// </summary>
[TestClass]
public sealed class ContractValidationTests
{
	/// <summary>
	/// Test case: feature requests reject unknown enums and invalid ids.
	/// </summary>
	[TestMethod]
	public void FeatureRequestsRejectUnknownEnumsAndInvalidIds()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailRequest(64, (ThumbnailMode)int.MaxValue));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewRequest(hydrationPolicy: (PreviewHydrationPolicy)int.MaxValue));
		Assert.Throws<ArgumentException>(() => new PropertyRequest(["System.Size", "System.Size"]));
		Assert.Throws<ArgumentException>(() => new PropertyRequest(["System.Size", " "]));
		Assert.Throws<ArgumentException>(() => new ThumbnailResult(ReadOnlyMemory<byte>.Empty, "image/png", false));
		Assert.Throws<ArgumentOutOfRangeException>(() => new StreamPreviewResult(new MemoryStream(), "text/plain", contentLength: -1));
	}

	/// <summary>
	/// Test case: thumbnail requests scale logical size to display pixels.
	/// </summary>
	[TestMethod]
	public void ThumbnailRequestsScaleLogicalSizeToDisplayPixels()
	{
		var request = new ThumbnailRequest(16, ThumbnailMode.Content, dpi: 144);

		Assert.AreEqual(16, request.RequestedSize);
		Assert.AreEqual(144, request.Dpi);
		Assert.AreEqual(24, request.RequestedPixelSize);
		Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailRequest(16, dpi: 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailRequest(4097));
	}

	/// <summary>
	/// Test case: view settings reject ambiguous or non finite values.
	/// </summary>
	[TestMethod]
	public void ViewSettingsRejectAmbiguousOrNonFiniteValues()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ViewColumnSettings("System.Size", double.NaN, 0));
		Assert.Throws<ArgumentException>(() => new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 100, 0), new ViewColumnSettings("System.Size", 120, 1),]));
		Assert.Throws<ArgumentException>(() => new BrowseViewSettings(columns: [new ViewColumnSettings("System.Size", 100, 0), new ViewColumnSettings("System.DateModified", 120, 0),]));
		Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseViewSettings(itemSize: double.PositiveInfinity));
		Assert.Throws<ArgumentException>(() => new BrowseViewSettings(groupPropertyId: " "));
		Assert.Throws<ArgumentOutOfRangeException>(() => new BrowseViewSettings(groupDirection: (ViewSortDirection)int.MaxValue));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Files.UITests.Views;

/// <summary>Tests preview-pane keyboard message classification.</summary>
[TestClass]
public sealed class PreviewPaneKeyboardTests
{
	/// <summary>Verifies the accelerator keys accepted from a Shell preview handler.</summary>
	[TestMethod]
	public void ForwardedAcceleratorsAreRestrictedToTheAdvertisedKeySet()
	{
		Assert.IsTrue(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0100, 0x09, false, false, true));
		Assert.IsTrue(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0100, 0x70, false, false, false));
		Assert.IsTrue(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0100, 0x41, true, false, false));
		Assert.IsTrue(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0104, 0x5A, false, true, false));
		Assert.IsFalse(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0100, 0x41, false, false, false));
		Assert.IsFalse(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0104, 0x73, false, false, false));
		Assert.IsFalse(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0100, 0x41, true, false, true));
		Assert.IsFalse(PreviewPane.IsSupportedForwardedPreviewAccelerator(0x0111, 0x41, true, false, false));
	}

	/// <summary>Verifies Explorer-compatible Tab and F6 focus cycling.</summary>
	[TestMethod]
	public void FocusCyclingRequiresKeyDownWithoutControl()
	{
		Assert.IsTrue(PreviewPane.IsPreviewFocusCycler(0x0100, 0x09, false));
		Assert.IsTrue(PreviewPane.IsPreviewFocusCycler(0x0100, 0x75, false));
		Assert.IsFalse(PreviewPane.IsPreviewFocusCycler(0x0100, 0x09, true));
		Assert.IsFalse(PreviewPane.IsPreviewFocusCycler(0x0104, 0x09, false));
		Assert.IsFalse(PreviewPane.IsPreviewFocusCycler(0x0100, 0x41, false));
	}
}

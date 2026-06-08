// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;

namespace Files.Core.Browsing;

public enum BrowsePreviewStatus
{
	Empty,
	Loading,
	Ready,
	Blocked,
	Unavailable,
	Failed,
}

public sealed record BrowsePreviewSnapshot(
	long RequestVersion,
	StorableKey? TargetKey,
	BrowsePreviewStatus Status,
	PreviewResult? Result = null,
	PreviewBlockReason? BlockReason = null,
	Exception? Error = null);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures.Previews;

namespace Files.Core.Browsing;

/// <summary>Describes the current state of a browse preview.</summary>
public enum BrowsePreviewStatus
{
	/// <summary>No preview request is active.</summary>
	Empty,
	/// <summary>A preview request is being processed.</summary>
	Loading,
	/// <summary>A preview is available.</summary>
	Ready,
	/// <summary>The preview was blocked by policy or an access condition.</summary>
	Blocked,
	/// <summary>No preview provider could produce a preview.</summary>
	Unavailable,
	/// <summary>Preview generation failed.</summary>
	Failed,
}

/// <summary>Captures the result of a browse preview request.</summary>
/// <param name="RequestVersion">The request version represented by the snapshot.</param>
/// <param name="TargetKey">The key of the item being previewed, if any.</param>
/// <param name="Status">The preview status.</param>
/// <param name="Result">The preview result when one is available.</param>
/// <param name="BlockReason">The reason the preview was blocked, if applicable.</param>
/// <param name="Error">The exception that caused a failed preview, if applicable.</param>
public sealed record BrowsePreviewSnapshot(long RequestVersion, StorableKey? TargetKey, BrowsePreviewStatus Status, PreviewResult? Result = null, PreviewBlockReason? BlockReason = null, Exception? Error = null);

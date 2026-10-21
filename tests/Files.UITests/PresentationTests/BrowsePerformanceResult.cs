// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;

namespace Files.UITests;

internal sealed class BrowsePerformanceResult
{
	public required string Scenario { get; init; }
	public string? Path { get; init; }
	public int ItemCount { get; init; }
	public double? TimeToFirstCoreBatchMs { get; init; }
	public double? TimeToFirstPresentedItemMs { get; init; }
	public double? TimeToFirstRealizedRowMs { get; init; }
	public double? EnumerationCompleteMs { get; init; }
	public double? TimeToFirstPropertiesMs { get; init; }
	public double? TimeToFirstThumbnailMs { get; init; }
	public double? FirstPagePropertiesCompleteMs { get; init; }
	public double? FirstPageThumbnailsCompleteMs { get; init; }
	public double? InitialPropertiesCompleteMs { get; init; }
	public double? InitialThumbnailsCompleteMs { get; init; }
	public double? InitialPresentationCompleteMs { get; init; }
	public int InitialPresentationItemCount { get; init; }
	public int ThumbnailUpdateCount { get; init; }
	public int RepeatedThumbnailUpdateCount { get; init; }
	public int FallbackThumbnailPublicationCount { get; init; }
	public int RepeatedFallbackThumbnailPublicationCount { get; init; }
	public int ContentThumbnailPublicationCount { get; init; }
	public double MaximumUILatencyMs { get; init; }
	public double P95UILatencyMs { get; init; }
	public int StallsOver16Ms { get; init; }
	public int StallsOver50Ms { get; init; }
	public int StallsOver100Ms { get; init; }
	public int DispatcherProbeCount { get; init; }
	public int DispatcherCallbackCount { get; init; }
	public int CollectionNotificationCount { get; init; }
	public int UniqueViewModelCount { get; init; }
	public int MaximumRealizedRowCount { get; init; }
	public required string CacheState { get; init; }
	public bool PropertiesEnabled { get; init; }
	public bool ThumbnailsEnabled { get; init; }
	public required BrowseEnvironmentMetadata Environment { get; init; }
}

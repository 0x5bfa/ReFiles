// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Transfers the identity and presentation size of a cached thumbnail.</summary>
public struct ThumbnailCacheKeyData
{
	/// <summary>The storage source identifier.</summary>
	public string SourceId;
	/// <summary>The source-specific item identifier.</summary>
	public string ItemId;
	/// <summary>The requested bitmap edge in physical pixels.</summary>
	public int RequestedSize;
	/// <summary>The thumbnail selection mode.</summary>
	public int Mode;
}

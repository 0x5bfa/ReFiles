// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>Identifies a storable item within a storage source.</summary>
/// <param name="SourceId">The storage source identifier.</param>
/// <param name="ItemId">The source-specific item identifier.</param>
public readonly record struct StorableKey(StorageSourceId SourceId, string ItemId);

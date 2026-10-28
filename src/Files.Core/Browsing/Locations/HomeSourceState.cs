// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Browsing;

/// <summary>Captures the independent root-loading state of one Home source.</summary>
/// <param name="SourceId">The storage source identity.</param>
/// <param name="DisplayName">The source display name.</param>
/// <param name="State">The current loading state.</param>
/// <param name="RootCount">The number of retained root references.</param>
/// <param name="Error">The most recent source error, when loading failed.</param>
public sealed record HomeSourceState(StorageSourceId SourceId, string DisplayName, HomeSourceLoadState State, int RootCount, Exception? Error);

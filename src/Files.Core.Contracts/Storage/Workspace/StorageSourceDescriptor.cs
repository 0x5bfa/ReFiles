// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Data;

/// <summary>Describes a configured source without granting provider or registration access.</summary>
/// <param name="SourceId">The stable configured source identity.</param>
/// <param name="SourceType">The source kind.</param>
/// <param name="DisplayName">The source display name.</param>
public sealed record StorageSourceDescriptor(StorageSourceId SourceId, string SourceType, string DisplayName);

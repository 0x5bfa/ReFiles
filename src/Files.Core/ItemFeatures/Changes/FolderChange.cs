// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Changes;

/// <summary>
/// Describes one folder change without exposing Windows Shell pointers.
/// </summary>
public sealed record FolderChange(FolderChangeKind Kind, StorableReference? CurrentItem, StorableReference? PreviousItem, bool RequiresRefresh);

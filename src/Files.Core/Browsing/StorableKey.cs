// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Browsing;

public readonly record struct StorableKey(StorageSourceId SourceId, string ItemId);

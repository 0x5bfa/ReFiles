// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Capabilities;

/// <summary>
/// Describes the storage item receiving optional capabilities.
/// </summary>
public sealed record ItemContext(IStorageSource Source, IStorable CoreModel, StorableReference Reference);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures;

/// <summary>
/// Describes the storage item receiving optional features.
/// </summary>
public sealed record ItemContext(IStorageSource Source, IStorable CoreModel, StorableReference Reference);

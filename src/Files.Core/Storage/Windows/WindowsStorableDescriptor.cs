// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Describes a Shell item without retaining an apartment-bound COM object.
/// </summary>
internal sealed record WindowsStorableDescriptor(string ItemId, StorageAddress Address, WindowsItemLocator Locator, WindowsStorableSnapshot Snapshot);

internal sealed record WindowsStorableDescriptorData(StorageAddress Address, WindowsItemLocator Locator, WindowsStorableSnapshot Snapshot);

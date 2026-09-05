// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Windows;

/// <summary>
/// Describes a Shell item without retaining an apartment-bound COM object.
/// </summary>
internal sealed record WindowsStorableDescriptor(string ItemId, StorageAddress Address, WindowsItemLocator Locator, WindowsStorableSnapshot Snapshot);

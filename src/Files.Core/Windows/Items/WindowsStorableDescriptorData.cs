// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;

namespace Files.Core.Windows;

internal sealed record WindowsStorableDescriptorData(StorageAddress Address, WindowsItemLocator Locator, WindowsStorableSnapshot Snapshot);

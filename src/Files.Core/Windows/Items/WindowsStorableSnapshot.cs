// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

/// <summary>
/// Contains apartment-neutral identity and display data copied from a Shell item.
/// </summary>
internal sealed record WindowsStorableSnapshot(string Name, string? FileSystemPath, bool IsFolder, bool IsStream, bool IsHidden);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

namespace Files.Core.Windows;

internal sealed record WindowsShellAppExtensionCommandToken(Guid ClassId, string VerbId, IReadOnlyList<int> SubCommandPath);

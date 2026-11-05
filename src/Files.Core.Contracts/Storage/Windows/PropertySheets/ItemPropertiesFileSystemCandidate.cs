// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="Path">The captured filesystem path.</param>
/// <param name="IsFolder">Whether the captured item is a folder.</param>
public sealed record ItemPropertiesFileSystemCandidate(string Path, bool IsFolder);

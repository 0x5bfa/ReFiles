// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="Reference">The captured storage item identity.</param>
/// <param name="Path">The captured filesystem path used for inspection.</param>
/// <param name="IsFolder">Whether the captured item is a folder.</param>
public sealed record ItemPropertiesFileSystemCandidate(StorableReference Reference, string Path, bool IsFolder);

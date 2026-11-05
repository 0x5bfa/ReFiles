// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Describes one physical archive record independently of its projected folder path.</summary>
/// <param name="Id">The reader's opaque record identifier, valid within the opened content.</param>
/// <param name="Path">The decoded original record path.</param>
/// <param name="IsDirectory">Whether the record describes a directory.</param>
/// <param name="Size">The uncompressed size in bytes.</param>
public sealed record ArchiveEntryInfo(string Id, string Path, bool IsDirectory, ulong Size);

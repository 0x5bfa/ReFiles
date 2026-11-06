// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="Paths">The existing filesystem paths in the selection.</param>
/// <param name="References">The storage identities corresponding to the existing filesystem paths.</param>
/// <param name="HasFolders">Whether the selection includes folders.</param>
/// <param name="IsSingleFile">Whether exactly one file is selected.</param>
/// <param name="IsSingleFolder">Whether exactly one ordinary folder is selected.</param>
/// <param name="IsDrive">Whether the selection identifies a volume root.</param>
/// <param name="IsReadOnly">The readonly state, or null for a mixed selection.</param>
/// <param name="IsHidden">The hidden state, or null for a mixed selection.</param>
/// <param name="IsArchive">The archive state, or null for a mixed selection.</param>
/// <param name="IsIndexed">The indexed state, or null for a mixed selection.</param>
/// <param name="IsCompressed">The compressed state, or null for a mixed selection.</param>
/// <param name="IsEncrypted">The encrypted state, or null for a mixed selection.</param>
/// <param name="Capabilities">The attribute operations supported by every selected path.</param>
public sealed record ItemPropertiesFileSystemSelection(IReadOnlyList<string> Paths, IReadOnlyList<StorableReference> References, bool HasFolders, bool IsSingleFile, bool IsSingleFolder,
	bool IsDrive, bool? IsReadOnly, bool? IsHidden, bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, WindowsFileAttributeCapabilities Capabilities);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Contains immutable filesystem property data.</summary>
/// <param name="IsDrive">Whether the selection identifies a volume root.</param>
/// <param name="InitialIsDriveCompressed">The compression state captured when editing began.</param>
/// <param name="InitialIsDriveIndexed">The indexing state captured when editing began.</param>
/// <param name="IsDriveCompressed">The requested volume compression state.</param>
/// <param name="IsDriveIndexed">The requested volume indexing state.</param>
/// <param name="IsReadOnly">The requested readonly state, or null to retain mixed values.</param>
/// <param name="IsHidden">The requested hidden state, or null to retain mixed values.</param>
/// <param name="IsArchive">The requested archive state, or null to retain mixed values.</param>
/// <param name="IsIndexed">The requested indexed state, or null to retain mixed values.</param>
/// <param name="IsCompressed">The requested compressed state, or null to retain mixed values.</param>
/// <param name="IsEncrypted">The requested encrypted state, or null to retain mixed values.</param>
/// <param name="UpdateCompression">Whether compression changes are included.</param>
/// <param name="UpdateEncryption">Whether encryption changes are included.</param>
/// <param name="ApplyToContents">Whether changes should include descendants.</param>
public sealed record ItemPropertiesFileSystemChanges(bool IsDrive, bool InitialIsDriveCompressed, bool InitialIsDriveIndexed, bool IsDriveCompressed, bool IsDriveIndexed,
	bool? IsReadOnly, bool? IsHidden, bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, bool UpdateCompression, bool UpdateEncryption, bool ApplyToContents);

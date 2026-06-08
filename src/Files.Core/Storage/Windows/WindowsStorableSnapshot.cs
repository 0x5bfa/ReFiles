// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>
/// Contains apartment-neutral identity and display data copied from a Shell item.
/// </summary>
internal sealed record WindowsStorableSnapshot(
	string ItemId,
	string Name,
	string? FileSystemPath,
	bool IsFolder,
	bool IsStream);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives.SevenZip;

internal sealed record SevenZipArchiveNode(
	string Path,
	string Name,
	bool IsDirectory,
	int? EntryIndex,
	ulong Size);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>
/// Identifies an item inside an archive independently of its active backend.
/// </summary>
public interface IArchiveEntry
{
	StorableReference Archive { get; }

	string EntryPath { get; }
}

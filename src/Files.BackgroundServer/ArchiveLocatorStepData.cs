// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.BackgroundServer;

/// <summary>Identifies one nested container record in a flat transfer value array.</summary>
public struct ArchiveLocatorStepData
{
	public string RecordId;
	public string EntryPath;
}

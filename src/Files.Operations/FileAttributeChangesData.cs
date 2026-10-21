// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

/// <summary>Transfers attribute changes; -1 preserves a value, 0 clears it, and 1 sets it.</summary>
public struct FileAttributeChangesData
{
	public int ReadOnly;
	public int Hidden;
	public int Archive;
	public int Indexed;
	public int Compressed;
	public int Encrypted;
	public bool ApplyToContents;
}

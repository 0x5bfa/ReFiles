// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

/// <summary>Transfers storage identity without exporting a Core model.</summary>
public struct StorageReferenceData
{
	public string SourceId;
	public string ItemId;
	public bool HasAddress;
	public string AddressScheme;
	public string AddressValue;
}

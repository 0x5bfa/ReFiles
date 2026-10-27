// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Describes acknowledged destination publication rather than inferred progress.</summary>
public enum StorageTransferPublication
{
	/// <summary>No destination publication was attempted.</summary>
	None,
	/// <summary>The destination item was published successfully.</summary>
	Committed,
	/// <summary>Publication was attempted without an acknowledged outcome.</summary>
	Unknown,
	/// <summary>Earlier items were published before another item failed or was canceled.</summary>
	Partial,
}

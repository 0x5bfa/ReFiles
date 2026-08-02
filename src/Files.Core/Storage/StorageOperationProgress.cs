// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Reports aggregate progress for a storage operation.
/// </summary>
public sealed record StorageOperationProgress
{
	public StorageOperationProgress(int CompletedItems, int TotalItems, StorableReference? CurrentItem = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(CompletedItems);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TotalItems);
		if (CompletedItems > TotalItems)
		{
			throw new ArgumentOutOfRangeException(nameof(CompletedItems), "Completed items cannot exceed the total item count.");
		}

		this.CompletedItems = CompletedItems;
		this.TotalItems = TotalItems;
		this.CurrentItem = CurrentItem;
	}

	public int CompletedItems { get; }

	public int TotalItems { get; }

	public StorableReference? CurrentItem { get; }
}

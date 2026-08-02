// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Reports aggregate progress for a storage operation.
/// </summary>
public sealed record StorageOperationProgress
{
	public int CompletedItems { get; }

	public int TotalItems { get; }

	public StorableReference? CurrentItem { get; }

	public StorageOperationProgress(int completedItems, int totalItems, StorableReference? currentItem = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(completedItems);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalItems);

		if (completedItems > totalItems)
		{
			throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed items cannot exceed the total item count.");
		}

		CompletedItems = completedItems;
		TotalItems = totalItems;
		CurrentItem = currentItem;
	}
}

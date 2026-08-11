// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Reports aggregate progress for a storage operation.
/// </summary>
public sealed record StorageOperationProgress
{
	/// <summary>Gets the number of items completed.</summary>
	public int CompletedItems { get; }

	/// <summary>Gets the total number of items.</summary>
	public int TotalItems { get; }

	/// <summary>Gets the item currently being processed.</summary>
	public StorableReference? CurrentItem { get; }

	/// <summary>Initializes operation progress data.</summary>
	/// <param name="completedItems">The number of completed items.</param>
	/// <param name="totalItems">The total item count.</param>
	/// <param name="currentItem">The item currently being processed.</param>
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

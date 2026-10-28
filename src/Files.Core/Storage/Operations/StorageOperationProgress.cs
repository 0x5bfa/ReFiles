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

	/// <summary>Gets the estimated number of bytes processed for the current item, when available.</summary>
	public long? CompletedBytes { get; }

	/// <summary>Gets the estimated total byte count for the current item, when available.</summary>
	public long? TotalBytes { get; }

	/// <summary>Initializes operation progress data.</summary>
	/// <param name="completedItems">The number of completed items.</param>
	/// <param name="totalItems">The total item count.</param>
	/// <param name="currentItem">The item currently being processed.</param>
	/// <param name="completedBytes">The estimated number of bytes processed for the current item.</param>
	/// <param name="totalBytes">The estimated total byte count for the current item.</param>
	public StorageOperationProgress(int completedItems, int totalItems, StorableReference? currentItem = null, long? completedBytes = null, long? totalBytes = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(completedItems);

		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalItems);

		if (completedItems > totalItems)
		{
			throw new ArgumentOutOfRangeException(nameof(completedItems), "Completed items cannot exceed the total item count.");
		}

		if (completedBytes.HasValue != totalBytes.HasValue)
		{
			throw new ArgumentException("Completed and total byte counts must either both be provided or both be omitted.");
		}

		if (completedBytes is { } currentBytes && totalBytes is { } allBytes)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(currentBytes);

			ArgumentOutOfRangeException.ThrowIfNegative(allBytes);

			if (currentBytes > allBytes)
			{
				throw new ArgumentOutOfRangeException(nameof(completedBytes), "Completed bytes cannot exceed the total byte count.");
			}
		}

		CompletedItems = completedItems;
		TotalItems = totalItems;
		CurrentItem = currentItem;
		CompletedBytes = completedBytes;
		TotalBytes = totalBytes;
	}
}

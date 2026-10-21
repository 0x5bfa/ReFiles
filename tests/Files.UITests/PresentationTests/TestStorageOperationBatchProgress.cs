// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Infrastructure;
using Files.StorageOperations;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace Files.UITests;

internal sealed class TestStorageOperationBatchProgress : IProgress<StorageOperationProgress>
{
	private readonly TestStorageOperationHandle _operation;
	private readonly int _completedItems;
	private readonly string _fallbackItemName;
	private readonly long? _completedBytesBeforeCurrentItem;
	private readonly long? _currentItemBytes;
	private readonly long? _totalBatchBytes;

	public TestStorageOperationBatchProgress(TestStorageOperationHandle operation, int completedItems, string fallbackItemName, long? completedBytesBeforeCurrentItem = null,
		long? currentItemBytes = null,
		long? totalBatchBytes = null)
	{
		ArgumentNullException.ThrowIfNull(operation);

		ArgumentException.ThrowIfNullOrWhiteSpace(fallbackItemName);

		ArgumentOutOfRangeException.ThrowIfNegative(completedBytesBeforeCurrentItem ?? 0);

		ArgumentOutOfRangeException.ThrowIfNegative(currentItemBytes ?? 0);

		ArgumentOutOfRangeException.ThrowIfNegative(totalBatchBytes ?? 0);

		_operation = operation;
		_completedItems = completedItems;
		_fallbackItemName = fallbackItemName;
		_completedBytesBeforeCurrentItem = completedBytesBeforeCurrentItem;
		_currentItemBytes = currentItemBytes;
		_totalBatchBytes = totalBatchBytes;
	}

	public void Report(StorageOperationProgress value)
	{
		ArgumentNullException.ThrowIfNull(value);

		var itemCompleted = value.CompletedItems == value.TotalItems ? 1 : 0;
		if (_completedBytesBeforeCurrentItem is { } completedBytesBeforeCurrentItem && _currentItemBytes is { } currentItemBytes && _totalBatchBytes is { } totalBatchBytes)
		{
			var currentItemCompletedBytes = itemCompleted is 1 ? currentItemBytes : Math.Clamp(value.CompletedBytes ?? 0, 0, currentItemBytes);
			_operation.Report(_completedItems + itemCompleted, GetItemName(value.CurrentItem), completedBytesBeforeCurrentItem + currentItemCompletedBytes, totalBatchBytes,
				isByteProgressForWholeOperation: true);

			return;
		}

		var completedBytes = itemCompleted is 0 ? value.CompletedBytes : null;
		var totalBytes = itemCompleted is 0 ? value.TotalBytes : null;
		_operation.Report(_completedItems + itemCompleted, GetItemName(value.CurrentItem), completedBytes, totalBytes);
	}

	private string GetItemName(StorableReference? reference)
	{
		if (reference?.LastKnownAddress is not { } address || string.IsNullOrWhiteSpace(address.Value))
		{
			return _fallbackItemName;
		}

		var trimmedAddress = address.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/');
		var itemName = Path.GetFileName(trimmedAddress);

		return string.IsNullOrWhiteSpace(itemName) ? _fallbackItemName : itemName;
	}
}

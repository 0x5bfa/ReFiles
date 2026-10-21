// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Files.StorageOperations;

namespace Files.UITests;

internal static class StorageOperationTrackerTestExtensions
{
	private static readonly ConditionalWeakTable<StorageOperationTracker, TestStorageOperationController> _sources = new();

	internal static TestStorageOperationHandle StartOperation(this StorageOperationTracker tracker, TrackedStorageOperationKind kind, int totalItems, string? currentItemName, bool canCancel,
		CancellationToken cancellationToken = default, string? destinationPath = null, bool canPause = false)
	{
		var source = _sources.GetValue(tracker, owner =>
		{
			var result = new TestStorageOperationController();
			owner.Attach(result);

			return result;
		});

		return source.StartOperation(kind, totalItems, currentItemName, canCancel, cancellationToken, destinationPath, canPause);
	}
}

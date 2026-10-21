// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using Files.Core.Storage;

namespace Files.Operations;

internal sealed class StorageOperationProgressReporter(OperationExecutionContext context, int index, int count, string fallbackName) : IProgress<StorageOperationProgress>
{
	public void Report(StorageOperationProgress value)
	{
		var completed = value.CompletedItems == value.TotalItems;
		var name = value.CurrentItem?.LastKnownAddress?.Value;
		name = string.IsNullOrEmpty(name) ? fallbackName : Path.GetFileName(name.TrimEnd('\\', '/'));
		context.Report(new OperationProgress(index + (completed ? 1 : 0), count, completed ? null : value.CompletedBytes, completed ? null : value.TotalBytes, name));
	}
}

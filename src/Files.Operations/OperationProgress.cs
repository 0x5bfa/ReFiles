// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Operations;

internal sealed record OperationProgress(long? CompletedItems = null, long? TotalItems = null, long? CompletedBytes = null, long? TotalBytes = null,
	string? CurrentItemName = null, bool IsByteProgressForWholeOperation = false);

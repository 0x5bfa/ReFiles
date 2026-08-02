// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Contains the outcome of a storage operation.
/// </summary>
public sealed record StorageOperationResult
{
	public bool Succeeded { get; }

	public StorableReference? ResultItem { get; }

	public Exception? Error { get; }

	public StorageOperationResult(bool succeeded, StorableReference? resultItem, Exception? error = null)
	{
		if (succeeded && error is not null)
		{
			throw new ArgumentException("A successful storage operation cannot contain an error.", nameof(error));
		}

		if (!succeeded && error is null)
		{
			throw new ArgumentNullException(nameof(error), "A failed storage operation requires an error.");
		}

		if (!succeeded && resultItem is not null)
		{
			throw new ArgumentException("A failed storage operation cannot publish a result item.", nameof(resultItem));
		}

		Succeeded = succeeded;
		ResultItem = resultItem;
		Error = error;
	}
}

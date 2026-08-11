// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Contains the outcome of a storage operation.
/// </summary>
public sealed record StorageOperationResult
{
	/// <summary>Gets a value indicating whether the operation succeeded.</summary>
	public bool Succeeded { get; }

	/// <summary>Gets the item produced by a successful operation.</summary>
	public StorableReference? ResultItem { get; }

	/// <summary>Gets the error produced by a failed operation.</summary>
	public Exception? Error { get; }

	/// <summary>Initializes an operation result.</summary>
	/// <param name="succeeded">Whether the operation succeeded.</param>
	/// <param name="resultItem">The item produced by the operation.</param>
	/// <param name="error">The error produced by a failed operation.</param>
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

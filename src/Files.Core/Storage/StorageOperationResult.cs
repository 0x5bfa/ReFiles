// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Contains the outcome of a storage operation.
/// </summary>
public sealed record StorageOperationResult
{
	public StorageOperationResult(
		bool Succeeded,
		StorableReference? ResultItem,
		Exception? Error = null)
	{
		if (Succeeded && Error is not null)
		{
			throw new ArgumentException(
				"A successful storage operation cannot contain an error.",
				nameof(Error));
		}

		if (!Succeeded && Error is null)
		{
			throw new ArgumentNullException(
				nameof(Error),
				"A failed storage operation requires an error.");
		}

		if (!Succeeded && ResultItem is not null)
		{
			throw new ArgumentException(
				"A failed storage operation cannot publish a result item.",
				nameof(ResultItem));
		}

		this.Succeeded = Succeeded;
		this.ResultItem = ResultItem;
		this.Error = Error;
	}

	public bool Succeeded { get; }

	public StorableReference? ResultItem { get; }

	public Exception? Error { get; }
}

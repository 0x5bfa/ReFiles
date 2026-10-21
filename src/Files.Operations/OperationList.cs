// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;

namespace Files.Operations;

/// <summary>Contains the server's operation summaries captured at one revision.</summary>
public sealed class OperationList
{
	private readonly OperationSummary[] _items;

	/// <summary>Gets the identity of this server process lifetime.</summary>
	public Guid ServerInstanceId { get; }

	/// <summary>Gets the revision captured with the items.</summary>
	public ulong Revision { get; }

	internal OperationList(Guid serverInstanceId, ulong revision, OperationSummary[] items)
	{
		ServerInstanceId = serverInstanceId;
		Revision = revision;
		_items = items;
	}

	/// <summary>Returns a value copy of the captured operation summaries.</summary>
	/// <returns>The operation summaries.</returns>
	public OperationSummary[] GetItems()
	{
		return (OperationSummary[])_items.Clone();
	}
}

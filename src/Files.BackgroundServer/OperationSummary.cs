// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;

namespace Files.BackgroundServer;

/// <summary>Contains a value copy of an operation's state, independent of its provider.</summary>
public struct OperationSummary
{
	/// <summary>The operation identity.</summary>
	public Guid Id;
	/// <summary>The stable operation kind, rather than a localized label.</summary>
	public string Kind;
	/// <summary>The acknowledged execution state.</summary>
	public OperationState State;
	/// <summary>The time at which the server accepted the operation.</summary>
	public DateTimeOffset AcceptedAt;
	/// <summary>Whether a terminal completion time is available.</summary>
	public bool HasCompletedAt;
	/// <summary>The terminal completion time when available.</summary>
	public DateTimeOffset CompletedAt;
	/// <summary>Whether cancellation has been requested.</summary>
	public bool IsCancellationRequested;
	/// <summary>Whether item counts are available.</summary>
	public bool HasItemProgress;
	/// <summary>The completed item count.</summary>
	public long CompletedItems;
	/// <summary>The total item count.</summary>
	public long TotalItems;
	/// <summary>Whether byte counts are available.</summary>
	public bool HasByteProgress;
	/// <summary>The completed byte count for the whole operation.</summary>
	public long CompletedBytes;
	/// <summary>The total byte count for the whole operation.</summary>
	public long TotalBytes;
	/// <summary>The current item name, or an empty string.</summary>
	public string CurrentItemName;
	/// <summary>The failure HRESULT, or zero.</summary>
	public int ErrorCode;
	/// <summary>Bounded diagnostic text, or an empty string.</summary>
	public string ErrorMessage;
	public string DestinationPath;
	public bool CanPause;
	public bool IsPauseRequested;
	public bool IsCancellationAcknowledged;
	public bool IsByteProgressForWholeOperation;
	public bool HasInterruption;
	public OperationInterruptionData Interruption;
	public bool HasResultItem;
	public StorageReferenceData ResultItem;
	public bool Skipped;
}

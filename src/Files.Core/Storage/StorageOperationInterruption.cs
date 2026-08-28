// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage;

/// <summary>Specifies the reason a storage operation requires a user decision.</summary>
public enum StorageOperationInterruptionKind
{
	/// <summary>The item is being used by another process.</summary>
	InUse,
	/// <summary>The current identity does not have access to the item.</summary>
	AccessDenied,
	/// <summary>The operation can continue with administrator rights.</summary>
	ElevationRequired,
	/// <summary>The destination does not have enough available space.</summary>
	DiskFull,
	/// <summary>The requested item or path no longer exists.</summary>
	NotFound,
	/// <summary>The item cannot be modified because it is read-only.</summary>
	ReadOnly,
	/// <summary>An item with the requested destination name already exists.</summary>
	NameConflict,
	/// <summary>The failure does not have a more specific interruption classification.</summary>
	Unexpected,
}

/// <summary>Specifies the responses available for a storage operation interruption.</summary>
[Flags]
public enum StorageOperationInterruptionResponses
{
	/// <summary>No response is available.</summary>
	None = 0,
	/// <summary>Retry the current item.</summary>
	Retry = 1 << 0,
	/// <summary>Skip the current item.</summary>
	Skip = 1 << 1,
	/// <summary>Cancel the entire operation.</summary>
	Cancel = 1 << 2,
	/// <summary>Continue the current item with administrator rights.</summary>
	Continue = 1 << 3,
	/// <summary>Confirm the requested action.</summary>
	Yes = 1 << 4,
	/// <summary>Reject the requested action.</summary>
	No = 1 << 5,
	/// <summary>Delete the conflicting item.</summary>
	Delete = 1 << 6,
	/// <summary>Acknowledge the interruption.</summary>
	Ok = 1 << 7,
}

/// <summary>Specifies the response selected for a storage operation interruption.</summary>
public enum StorageOperationInterruptionDecision
{
	/// <summary>Retry the current item.</summary>
	Retry,
	/// <summary>Skip the current item.</summary>
	Skip,
	/// <summary>Cancel the entire operation.</summary>
	Cancel,
	/// <summary>Continue the current item with administrator rights.</summary>
	Continue,
	/// <summary>Confirm the requested action.</summary>
	Yes,
	/// <summary>Reject the requested action.</summary>
	No,
	/// <summary>Delete the conflicting item.</summary>
	Delete,
	/// <summary>Acknowledge the interruption.</summary>
	Ok,
}

/// <summary>Describes a storage operation interruption that requires a user decision.</summary>
public sealed record StorageOperationInterruption
{
	/// <summary>Gets the interruption kind.</summary>
	public StorageOperationInterruptionKind Kind { get; }

	/// <summary>Gets the responses that may be selected.</summary>
	public StorageOperationInterruptionResponses AvailableResponses { get; }

	/// <summary>Gets the native or backend-specific error code.</summary>
	public int ErrorCode { get; }

	/// <summary>Gets the name of the affected item when it is known.</summary>
	public string? ItemName { get; }

	/// <summary>Gets the destination path when it is relevant and known.</summary>
	public string? DestinationPath { get; }

	/// <summary>Gets a value indicating whether the decision may be applied to remaining matching interruptions.</summary>
	public bool CanApplyToAll { get; }

	/// <summary>Initializes a storage operation interruption.</summary>
	/// <param name="kind">The interruption kind.</param>
	/// <param name="availableResponses">The responses that may be selected.</param>
	/// <param name="errorCode">The native or backend-specific error code.</param>
	/// <param name="itemName">The optional affected item name.</param>
	/// <param name="destinationPath">The optional destination path.</param>
	/// <param name="canApplyToAll">Whether the decision may be applied to remaining matching interruptions.</param>
	public StorageOperationInterruption(StorageOperationInterruptionKind kind, StorageOperationInterruptionResponses availableResponses, int errorCode = 0, string? itemName = null,
		string? destinationPath = null, bool canApplyToAll = true)
	{
		if (availableResponses is StorageOperationInterruptionResponses.None)
		{
			throw new ArgumentOutOfRangeException(nameof(availableResponses));
		}

		Kind = kind;
		AvailableResponses = availableResponses;
		ErrorCode = errorCode;
		ItemName = itemName;
		DestinationPath = destinationPath;
		CanApplyToAll = canApplyToAll;
	}
}

/// <summary>Contains the response selected for a storage operation interruption.</summary>
public readonly record struct StorageOperationInterruptionResponse
{
	/// <summary>Gets the selected decision.</summary>
	public StorageOperationInterruptionDecision Decision { get; }

	/// <summary>Gets a value indicating whether the decision applies to remaining matching interruptions.</summary>
	public bool ApplyToAll { get; }

	/// <summary>Initializes a storage operation interruption response.</summary>
	/// <param name="decision">The selected decision.</param>
	/// <param name="applyToAll">Whether the decision applies to remaining matching interruptions.</param>
	public StorageOperationInterruptionResponse(StorageOperationInterruptionDecision decision, bool applyToAll = false)
	{
		Decision = decision;
		ApplyToAll = applyToAll;
	}
}

/// <summary>Represents a storage failure that can be resolved through an interruption response.</summary>
public sealed class StorageOperationInterruptedException : IOException
{
	/// <summary>Gets the interruption that describes the required decision.</summary>
	public StorageOperationInterruption Interruption { get; }

	/// <summary>Initializes a storage operation interruption exception.</summary>
	/// <param name="message">The error message.</param>
	/// <param name="interruption">The required interruption decision.</param>
	public StorageOperationInterruptedException(string message, StorageOperationInterruption interruption)
		: base(message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		ArgumentNullException.ThrowIfNull(interruption);

		Interruption = interruption;
		HResult = interruption.ErrorCode;
	}
}

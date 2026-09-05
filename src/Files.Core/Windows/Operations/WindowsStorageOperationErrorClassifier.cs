// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using Files.Core.Storage;
using Windows.Win32.Foundation;

namespace Files.Core.Windows;

internal static class WindowsStorageOperationErrorClassifier
{
	private const int AccessDeniedHResult = unchecked((int)0x80070005);
	private const int DiskFullHResult = unchecked((int)0x80070070);
	private const int FileNotFoundHResult = unchecked((int)0x80070002);
	private const int LockViolationHResult = unchecked((int)0x80070021);
	private const int PathNotFoundHResult = unchecked((int)0x80070003);
	private const int SharingViolationHResult = unchecked((int)0x80070020);
	private const int CopyEngineRequiresElevation = unchecked((int)0x80270002);
	private const int CopyEngineDestinationReadOnlyDiscStart = unchecked((int)0x8027000F);
	private const int CopyEngineDestinationReadOnlyDiscEnd = unchecked((int)0x80270014);
	private const int CopyEngineAccessDeniedSource = unchecked((int)0x80270021);
	private const int CopyEngineAccessDeniedDestination = unchecked((int)0x80270022);
	private const int CopyEnginePathNotFoundSource = unchecked((int)0x80270023);
	private const int CopyEnginePathNotFoundDestination = unchecked((int)0x80270024);
	private const int CopyEngineSharingViolationSource = unchecked((int)0x80270027);
	private const int CopyEngineSharingViolationDestination = unchecked((int)0x80270028);
	private const int CopyEngineAlreadyExistsStart = unchecked((int)0x80270029);
	private const int CopyEngineAlreadyExistsEnd = unchecked((int)0x8027002C);
	private const int CopyEngineDiskFull = unchecked((int)0x80270032);
	private const int CopyEngineDiskFullClean = unchecked((int)0x80270033);
	private const int CopyEngineAccessDeniedReadOnly = unchecked((int)0x8027003F);

	private const StorageOperationInterruptionResponses StandardResponses = StorageOperationInterruptionResponses.Retry | StorageOperationInterruptionResponses.Skip
		| StorageOperationInterruptionResponses.Cancel;
	private const StorageOperationInterruptionResponses ElevationResponses = StorageOperationInterruptionResponses.Continue | StorageOperationInterruptionResponses.Skip
		| StorageOperationInterruptionResponses.Cancel;
	private const StorageOperationInterruptionResponses ConflictResponses = StorageOperationInterruptionResponses.Yes | StorageOperationInterruptionResponses.No
		| StorageOperationInterruptionResponses.Cancel;

	internal static StorageOperationInterruptedException Create(HRESULT result, string operationName, StorableReference item, string? destinationPath = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

		ArgumentNullException.ThrowIfNull(item);

		var itemName = GetItemName(item);
		var kind = Classify(result.Value);
		var responses = GetResponses(kind);
		var interruption = new StorageOperationInterruption(kind, responses, result.Value, itemName, destinationPath);

		return new StorageOperationInterruptedException($"The Windows Shell {operationName} operation requires a user decision. HRESULT={result}.", interruption);
	}

	internal static StorageOperationInterruption CreateNameConflict(string itemName, string destinationPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(itemName);

		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

		return new StorageOperationInterruption(StorageOperationInterruptionKind.NameConflict, ConflictResponses, CopyEngineAlreadyExistsStart, itemName, destinationPath);
	}

	internal static bool IsDecisionAvailable(StorageOperationInterruption interruption, StorageOperationInterruptionDecision decision)
	{
		ArgumentNullException.ThrowIfNull(interruption);

		var response = decision switch
		{
			StorageOperationInterruptionDecision.Retry => StorageOperationInterruptionResponses.Retry,
			StorageOperationInterruptionDecision.Skip => StorageOperationInterruptionResponses.Skip,
			StorageOperationInterruptionDecision.Cancel => StorageOperationInterruptionResponses.Cancel,
			StorageOperationInterruptionDecision.Continue => StorageOperationInterruptionResponses.Continue,
			StorageOperationInterruptionDecision.Yes => StorageOperationInterruptionResponses.Yes,
			StorageOperationInterruptionDecision.No => StorageOperationInterruptionResponses.No,
			StorageOperationInterruptionDecision.Delete => StorageOperationInterruptionResponses.Delete,
			StorageOperationInterruptionDecision.Ok => StorageOperationInterruptionResponses.Ok,
			_ => StorageOperationInterruptionResponses.None,
		};

		return interruption.AvailableResponses.HasFlag(response);
	}

	internal static StorageOperationInterruptionKind Classify(int errorCode)
	{
		if (errorCode is SharingViolationHResult or LockViolationHResult or CopyEngineSharingViolationSource or CopyEngineSharingViolationDestination)
		{
			return StorageOperationInterruptionKind.InUse;
		}

		if (errorCode is CopyEngineAccessDeniedSource)
		{
			return StorageOperationInterruptionKind.AccessDenied;
		}

		if (errorCode is AccessDeniedHResult or CopyEngineAccessDeniedDestination or CopyEngineRequiresElevation)
		{
			return StorageOperationInterruptionKind.ElevationRequired;
		}

		if (errorCode is DiskFullHResult or CopyEngineDiskFull or CopyEngineDiskFullClean)
		{
			return StorageOperationInterruptionKind.DiskFull;
		}

		if (errorCode is FileNotFoundHResult or PathNotFoundHResult or CopyEnginePathNotFoundSource or CopyEnginePathNotFoundDestination)
		{
			return StorageOperationInterruptionKind.NotFound;
		}

		if (errorCode is CopyEngineAccessDeniedReadOnly || errorCode >= CopyEngineDestinationReadOnlyDiscStart && errorCode <= CopyEngineDestinationReadOnlyDiscEnd)
		{
			return StorageOperationInterruptionKind.ReadOnly;
		}

		if (errorCode >= CopyEngineAlreadyExistsStart && errorCode <= CopyEngineAlreadyExistsEnd)
		{
			return StorageOperationInterruptionKind.NameConflict;
		}

		return StorageOperationInterruptionKind.Unexpected;
	}

	private static StorageOperationInterruptionResponses GetResponses(StorageOperationInterruptionKind kind)
	{
		return kind switch
		{
			StorageOperationInterruptionKind.ElevationRequired => ElevationResponses,
			StorageOperationInterruptionKind.NameConflict => ConflictResponses,
			_ => StandardResponses,
		};
	}

	private static string? GetItemName(StorableReference item)
	{
		if (item.LastKnownAddress is not { } address || string.IsNullOrWhiteSpace(address.Value))
		{
			return null;
		}

		try
		{
			var trimmedPath = address.Value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var name = Path.GetFileName(trimmedPath);

			return string.IsNullOrWhiteSpace(name) ? address.Value : name;
		}
		catch (ArgumentException)
		{
			return address.Value;
		}
	}
}

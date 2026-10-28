// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Requests that one item be moved into a destination folder.
/// </summary>
public sealed record MoveOperationRequest : StorageOperationRequest
{
	/// <summary>Gets the item to move.</summary>
	public StorageItemLocator Source { get; }

	/// <summary>Gets the destination folder.</summary>
	public StorableReference DestinationFolder { get; }

	/// <summary>Gets the optional destination name.</summary>
	public string? NewName { get; }

	/// <summary>Gets the conflict behavior.</summary>
	public StorageConflictBehavior ConflictBehavior { get; }

	/// <summary>Initializes a move request.</summary>
	/// <param name="item">The item to move.</param>
	/// <param name="destinationFolder">The destination folder.</param>
	/// <param name="newName">The optional destination name.</param>
	/// <param name="conflictBehavior">The conflict behavior.</param>
	public MoveOperationRequest(StorableReference item, StorableReference destinationFolder, string? newName = null, StorageConflictBehavior conflictBehavior = StorageConflictBehavior.Fail)
		: this(new ConfiguredItemLocator(item), destinationFolder, newName, conflictBehavior)
	{
	}

	/// <summary>Creates a transfer request from independently resolvable source values.</summary>
	/// <param name="item">The captured source locator.</param>
	/// <param name="destinationFolder">The configured destination folder.</param>
	/// <param name="newName">The optional destination name.</param>
	/// <param name="conflictBehavior">The destination conflict policy.</param>
	public MoveOperationRequest(StorageItemLocator item, StorableReference destinationFolder, string? newName = null, StorageConflictBehavior conflictBehavior = StorageConflictBehavior.Fail)
	{
		ArgumentNullException.ThrowIfNull(item);

		ArgumentNullException.ThrowIfNull(destinationFolder);

		if (newName is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		}

		ValidateConflictBehavior(conflictBehavior);
		Source = item;
		DestinationFolder = destinationFolder;
		NewName = newName;
		ConflictBehavior = conflictBehavior;
	}

	private static void ValidateConflictBehavior(StorageConflictBehavior conflictBehavior)
	{
		if (conflictBehavior is not StorageConflictBehavior.Fail and not StorageConflictBehavior.GenerateUniqueName and not StorageConflictBehavior.Prompt)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}
	}
}

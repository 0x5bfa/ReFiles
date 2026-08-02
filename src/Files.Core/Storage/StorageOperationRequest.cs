// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Describes one storage operation requested by the application.
/// </summary>
public abstract record StorageOperationRequest;

public enum StorageConflictBehavior
{
	Fail,
	GenerateUniqueName,
}

public enum StorageItemKind
{
	File,
	Folder,
}

/// <summary>
/// Requests that one item be renamed within its current parent.
/// </summary>
public sealed record RenameOperationRequest : StorageOperationRequest
{
	public StorableReference Item { get; }

	public string NewName { get; }

	public RenameOperationRequest(StorableReference item, string newName)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		Item = item;
		NewName = newName;
	}
}

/// <summary>
/// Requests a new empty file or folder under a parent folder.
/// </summary>
public sealed record CreateItemOperationRequest : StorageOperationRequest
{
	public StorableReference Parent { get; }

	public string Name { get; }

	public StorageItemKind Kind { get; }

	public StorageConflictBehavior ConflictBehavior { get; }

	public CreateItemOperationRequest(StorableReference parent, string name, StorageItemKind kind, StorageConflictBehavior conflictBehavior = StorageConflictBehavior.Fail)
	{
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (kind is not StorageItemKind.File and not StorageItemKind.Folder)
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		ValidateConflictBehavior(conflictBehavior);

		Parent = parent;
		Name = name;
		Kind = kind;
		ConflictBehavior = conflictBehavior;
	}

	private static void ValidateConflictBehavior(StorageConflictBehavior conflictBehavior)
	{
		if (conflictBehavior is not StorageConflictBehavior.Fail and not StorageConflictBehavior.GenerateUniqueName)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}
	}
}

/// <summary>
/// Requests that one item be copied into a destination folder.
/// </summary>
public sealed record CopyOperationRequest : StorageOperationRequest
{
	public StorableReference Item { get; }

	public StorableReference DestinationFolder { get; }

	public string? NewName { get; }

	public StorageConflictBehavior ConflictBehavior { get; }

	public CopyOperationRequest(StorableReference item, StorableReference destinationFolder, string? newName = null, StorageConflictBehavior conflictBehavior = StorageConflictBehavior.Fail)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentNullException.ThrowIfNull(destinationFolder);

		if (newName is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		}

		ValidateConflictBehavior(conflictBehavior);
		Item = item;
		DestinationFolder = destinationFolder;
		NewName = newName;
		ConflictBehavior = conflictBehavior;
	}

	private static void ValidateConflictBehavior(StorageConflictBehavior conflictBehavior)
	{
		if (conflictBehavior is not StorageConflictBehavior.Fail and not StorageConflictBehavior.GenerateUniqueName)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}
	}
}

/// <summary>
/// Requests that one item be moved into a destination folder.
/// </summary>
public sealed record MoveOperationRequest : StorageOperationRequest
{
	public StorableReference Item { get; }

	public StorableReference DestinationFolder { get; }

	public string? NewName { get; }

	public StorageConflictBehavior ConflictBehavior { get; }

	public MoveOperationRequest(StorableReference item, StorableReference destinationFolder, string? newName = null, StorageConflictBehavior conflictBehavior = StorageConflictBehavior.Fail)
	{
		ArgumentNullException.ThrowIfNull(item);
		ArgumentNullException.ThrowIfNull(destinationFolder);

		if (newName is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		}

		ValidateConflictBehavior(conflictBehavior);
		Item = item;
		DestinationFolder = destinationFolder;
		NewName = newName;
		ConflictBehavior = conflictBehavior;
	}

	private static void ValidateConflictBehavior(StorageConflictBehavior conflictBehavior)
	{
		if (conflictBehavior is not StorageConflictBehavior.Fail and not StorageConflictBehavior.GenerateUniqueName)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}
	}
}

/// <summary>
/// Requests that one item be deleted or moved to the Recycle Bin.
/// </summary>
public sealed record DeleteOperationRequest : StorageOperationRequest
{
	public StorableReference Item { get; }

	public bool Permanently { get; }

	public DeleteOperationRequest(StorableReference item, bool permanently = false)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
		Permanently = permanently;
	}
}

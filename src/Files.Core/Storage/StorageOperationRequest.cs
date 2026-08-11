// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Describes one storage operation requested by the application.
/// </summary>
public abstract record StorageOperationRequest;

/// <summary>Specifies how an operation handles an existing destination name.</summary>
public enum StorageConflictBehavior
{
	/// <summary>Fail when the destination name already exists.</summary>
	Fail,
	/// <summary>Generate a unique destination name.</summary>
	GenerateUniqueName,
}

/// <summary>Specifies the kind of item to create.</summary>
public enum StorageItemKind
{
	/// <summary>Create a file.</summary>
	File,
	/// <summary>Create a folder.</summary>
	Folder,
}

/// <summary>
/// Requests that one item be renamed within its current parent.
/// </summary>
public sealed record RenameOperationRequest : StorageOperationRequest
{
	/// <summary>Gets the item to rename.</summary>
	public StorableReference Item { get; }

	/// <summary>Gets the new item name.</summary>
	public string NewName { get; }

	/// <summary>Initializes a rename request.</summary>
	/// <param name="item">The item to rename.</param>
	/// <param name="newName">The new item name.</param>
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
	/// <summary>Gets the parent folder.</summary>
	public StorableReference Parent { get; }

	/// <summary>Gets the new item name.</summary>
	public string Name { get; }

	/// <summary>Gets the item kind to create.</summary>
	public StorageItemKind Kind { get; }

	/// <summary>Gets the conflict behavior.</summary>
	public StorageConflictBehavior ConflictBehavior { get; }

	/// <summary>Initializes a create-item request.</summary>
	/// <param name="parent">The parent folder.</param>
	/// <param name="name">The new item name.</param>
	/// <param name="kind">The item kind to create.</param>
	/// <param name="conflictBehavior">The conflict behavior.</param>
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
	/// <summary>Gets the item to copy.</summary>
	public StorableReference Item { get; }

	/// <summary>Gets the destination folder.</summary>
	public StorableReference DestinationFolder { get; }

	/// <summary>Gets the optional destination name.</summary>
	public string? NewName { get; }

	/// <summary>Gets the conflict behavior.</summary>
	public StorageConflictBehavior ConflictBehavior { get; }

	/// <summary>Initializes a copy request.</summary>
	/// <param name="item">The item to copy.</param>
	/// <param name="destinationFolder">The destination folder.</param>
	/// <param name="newName">The optional destination name.</param>
	/// <param name="conflictBehavior">The conflict behavior.</param>
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
	/// <summary>Gets the item to move.</summary>
	public StorableReference Item { get; }

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
	/// <summary>Gets the item to delete.</summary>
	public StorableReference Item { get; }

	/// <summary>Gets a value indicating whether the item should bypass the Recycle Bin.</summary>
	public bool Permanently { get; }

	/// <summary>Initializes a delete request.</summary>
	/// <param name="item">The item to delete.</param>
	/// <param name="permanently">Whether to delete the item permanently.</param>
	public DeleteOperationRequest(StorableReference item, bool permanently = false)
	{
		ArgumentNullException.ThrowIfNull(item);

		Item = item;
		Permanently = permanently;
	}
}

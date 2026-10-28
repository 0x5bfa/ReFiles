// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

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
		if (conflictBehavior is not StorageConflictBehavior.Fail and not StorageConflictBehavior.GenerateUniqueName and not StorageConflictBehavior.Prompt)
		{
			throw new ArgumentOutOfRangeException(nameof(conflictBehavior));
		}
	}
}

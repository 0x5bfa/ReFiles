// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>Inspects filesystem metadata and submits explicit attribute updates.</summary>
public interface IItemPropertiesFileSystem
{
	/// <summary>Inspects the existing paths in a selection.</summary>
	/// <param name="candidates">The captured paths.</param>
	/// <returns>The selection metadata.</returns>
	ItemPropertiesFileSystemSelection Inspect(IReadOnlyList<ItemPropertiesFileSystemCandidate> candidates);
	/// <summary>Reads aggregate sizes, dates and drive information.</summary>
	/// <param name="paths">The inspected paths.</param>
	/// <param name="cancellationToken">Cancels enumeration.</param>
	/// <returns>The aggregate metadata.</returns>
	ItemPropertiesFileSystemMetadata ReadMetadata(IReadOnlyList<string> paths, CancellationToken cancellationToken);
	/// <summary>Submits attribute changes through the operation executor.</summary>
	/// <param name="references">The affected storage items.</param>
	/// <param name="changes">The requested changes.</param>
	/// <param name="cancellationToken">Cancels before acceptance.</param>
	void Apply(IReadOnlyList<StorableReference> references, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken);
}

/// <summary>Submits filesystem attribute edits to the owning operation host.</summary>
public interface IFileAttributeOperationService
{
	/// <summary>Submits the requested attribute changes.</summary>
	/// <param name="references">The affected storage items.</param>
	/// <param name="changes">The requested changes.</param>
	/// <param name="cancellationToken">Cancels admission or observation.</param>
	/// <returns>The operation completion.</returns>
	Task ApplyAttributesAsync(IReadOnlyList<StorableReference> references, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken);
}

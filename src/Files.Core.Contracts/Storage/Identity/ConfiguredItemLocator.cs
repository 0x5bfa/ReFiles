// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Identifies an item in a configured storage source.</summary>
public sealed record ConfiguredItemLocator : StorageItemLocator
{
	/// <summary>Gets the configured item reference.</summary>
	public StorableReference Reference { get; }

	/// <inheritdoc />
	public override StorableReference Root => Reference;

	/// <summary>Creates a configured item locator.</summary>
	/// <param name="reference">The configured item reference.</param>
	public ConfiguredItemLocator(StorableReference reference)
	{
		ArgumentNullException.ThrowIfNull(reference);

		Reference = reference;
	}
}

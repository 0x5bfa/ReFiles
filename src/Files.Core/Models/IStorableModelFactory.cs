// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>Creates presentation models from storage-layer items.</summary>
public interface IStorableModelFactory
{
	/// <summary>Creates a model for a storage item.</summary>
	/// <param name="source">The storage source that owns the item.</param>
	/// <param name="coreModel">The storage-layer item.</param>
	/// <returns>The corresponding presentation model.</returns>
	IStorableModel Create(IStorageSource source, IStorable coreModel);
}

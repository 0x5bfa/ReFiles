// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;

namespace Files.Core.Models;

/// <summary>
/// Files-specific application model for an OwlCore storage item.
/// </summary>
public interface IStorableModel : IHasItemFeatures, IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Gets the stable Files reference for the item.
	/// </summary>
	StorableReference Reference { get; }

	/// <summary>
	/// Gets the item name captured when the model was created.
	/// </summary>
	string Name { get; }
}

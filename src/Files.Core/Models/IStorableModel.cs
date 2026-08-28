// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Storage;

namespace Files.Core.Models;

/// <summary>
/// Files-specific application model for an OwlCore storage item.
/// </summary>
public interface IStorableModel : IHasCapabilities, IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Gets the stable Files reference for the item.
	/// </summary>
	StorableReference Reference { get; }

	/// <summary>
	/// Gets the item name captured when the model was created.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Gets a value indicating whether the storage item is marked as hidden.
	/// </summary>
	bool IsHidden => false;
}

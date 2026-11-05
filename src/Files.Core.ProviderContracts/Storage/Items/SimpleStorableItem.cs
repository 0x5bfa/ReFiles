// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Provides a minimal storage item containing only identity and name.
/// </summary>
public sealed class SimpleStorableItem : IStorable
{
	/// <summary>
	/// Initializes a simple storage item.
	/// </summary>
	/// <param name="id">The stable item identifier.</param>
	/// <param name="name">The item name.</param>
	public SimpleStorableItem(string id, string name)
	{
		Id = id;
		Name = name;
	}

	/// <summary>
	/// Gets the stable item identifier.
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Gets the item name.
	/// </summary>
	public string Name { get; }
}

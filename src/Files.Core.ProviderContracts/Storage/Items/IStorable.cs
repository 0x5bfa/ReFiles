// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Represents an item that can be stored or retrieved from a storage source.
/// </summary>
public interface IStorable
{
	/// <summary>
	/// Gets a unique identifier for this item that is consistent across reruns.
	/// </summary>
	string Id { get; }

	/// <summary>
	/// Gets the name of the item, including its extension when it has one.
	/// </summary>
	string Name { get; }
}

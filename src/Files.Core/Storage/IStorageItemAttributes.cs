// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Provides basic attributes without exposing a provider implementation.</summary>
public interface IStorageItemAttributes
{
	/// <summary>Gets whether the item is hidden.</summary>
	bool IsHidden { get; }
}

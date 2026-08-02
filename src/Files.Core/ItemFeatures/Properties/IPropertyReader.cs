// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;

namespace Files.Core.ItemFeatures.Properties;

/// <summary>
/// Reads properties for a batch of items owned by a storage source or extension.
/// </summary>
public interface IPropertyReader
{
	bool CanRead(ItemContext context);

	ValueTask<IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>> GetPropertiesAsync(PropertyRequest request, IReadOnlyList<ItemContext> contexts, CancellationToken cancellationToken = default);
}

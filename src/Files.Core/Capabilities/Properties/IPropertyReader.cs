// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Storage;

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Reads properties for a batch of items owned by a storage source or extension.
/// </summary>
public interface IPropertyReader
{
	/// <summary>Determines whether this reader can read the supplied context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns><see langword="true"/> when the reader applies.</returns>
	bool CanRead(ItemContext context);

	/// <summary>Reads properties for a batch of item contexts.</summary>
	/// <param name="request">The requested properties.</param>
	/// <param name="contexts">The item contexts to read.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>Properties grouped by item reference.</returns>
	ValueTask<IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>> GetPropertiesAsync(PropertyRequest request, IReadOnlyList<ItemContext> contexts, CancellationToken cancellationToken = default);
}

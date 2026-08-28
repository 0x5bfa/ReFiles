// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Reads properties bound to one application model.
/// </summary>
public interface IPropertySource
{
	/// <summary>Reads the requested properties for the bound item.</summary>
	/// <param name="request">The requested properties.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The requested property values.</returns>
	ValueTask<IReadOnlyDictionary<string, object?>> GetPropertiesAsync(PropertyRequest request, CancellationToken cancellationToken = default);
}

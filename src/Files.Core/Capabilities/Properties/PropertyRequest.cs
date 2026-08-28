// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Describes the property values required by a consumer.
/// </summary>
public sealed record PropertyRequest
{
	/// <summary>Gets the unique property identifiers requested by the consumer.</summary>
	public IReadOnlyList<string> PropertyIds { get; }

	/// <summary>Initializes a property request.</summary>
	/// <param name="propertyIds">The unique property identifiers to request.</param>
	public PropertyRequest(IEnumerable<string> propertyIds)
	{
		ArgumentNullException.ThrowIfNull(propertyIds);

		var values = propertyIds.ToArray();
		if (values.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException("Property IDs cannot contain null or whitespace values.", nameof(propertyIds));
		}

		if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
		{
			throw new ArgumentException("Property IDs must be unique.", nameof(propertyIds));
		}

		PropertyIds = Array.AsReadOnly(values);
	}
}

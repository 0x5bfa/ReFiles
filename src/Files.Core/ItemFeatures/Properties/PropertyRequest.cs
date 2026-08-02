// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Properties;

/// <summary>
/// Describes the property values required by a consumer.
/// </summary>
public sealed record PropertyRequest
{
	public PropertyRequest(IEnumerable<string> PropertyIds)
	{
		ArgumentNullException.ThrowIfNull(PropertyIds);

		var values = PropertyIds.ToArray();
		if (values.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException("Property IDs cannot contain null or whitespace values.", nameof(PropertyIds));
		}

		if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
		{
			throw new ArgumentException("Property IDs must be unique.", nameof(PropertyIds));
		}

		this.PropertyIds = Array.AsReadOnly(values);
	}

	public IReadOnlyList<string> PropertyIds { get; }
}

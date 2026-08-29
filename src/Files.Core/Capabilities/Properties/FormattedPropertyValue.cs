// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities.Properties;

/// <summary>
/// Contains a raw property value and the text formatted for display by its source.
/// </summary>
public sealed record FormattedPropertyValue
{
	/// <summary>Gets the raw value used for sorting and grouping.</summary>
	public object? RawValue { get; }

	/// <summary>Gets the source-formatted display text.</summary>
	public string DisplayText { get; }

	/// <summary>Initializes a formatted property value.</summary>
	/// <param name="rawValue">The raw property value.</param>
	/// <param name="displayText">The text formatted for display by the property source.</param>
	public FormattedPropertyValue(object? rawValue, string displayText)
	{
		ArgumentNullException.ThrowIfNull(displayText);

		RawValue = rawValue;
		DisplayText = displayText;
	}
}

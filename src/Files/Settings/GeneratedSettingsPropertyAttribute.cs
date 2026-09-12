// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Settings;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class GeneratedSettingsPropertyAttribute : Attribute
{
	public GeneratedSettingsPropertyAttribute(object defaultValue)
	{
		DefaultValue = defaultValue;
	}

	public object DefaultValue { get; }

	public object? MinValue { get; set; }

	public object? MaxValue { get; set; }
}

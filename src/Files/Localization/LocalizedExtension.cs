// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Markup;
using System;

namespace Files.Localization;

/// <summary>
/// Resolves an application string resource for use in XAML.
/// </summary>
public sealed partial class LocalizedExtension : MarkupExtension
{
	/// <summary>
	/// Gets or sets the resource key to resolve.
	/// </summary>
	public string ResourceKey { get; set; } = string.Empty;

	/// <inheritdoc />
	protected override object ProvideValue()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ResourceKey);

		return ResourceKey.GetLocalized();
	}
}

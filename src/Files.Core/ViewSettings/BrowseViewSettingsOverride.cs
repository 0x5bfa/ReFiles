// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ViewSettings;

/// <summary>Contains a partial layer of browse view settings.</summary>
public sealed record BrowseViewSettingsOverride
{
	/// <summary>Gets the settings supplied by this override.</summary>
	public ViewSettingsOverrideFields Fields { get; }

	/// <summary>Gets the values used for fields supplied by this override.</summary>
	public BrowseViewSettings Values { get; }

	/// <summary>Initializes a partial browse view settings override.</summary>
	/// <param name="fields">The settings supplied by the override.</param>
	/// <param name="values">The values for supplied settings.</param>
	public BrowseViewSettingsOverride(ViewSettingsOverrideFields fields, BrowseViewSettings values)
	{
		if ((fields & ~ViewSettingsOverrideFields.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fields));
		}

		ArgumentNullException.ThrowIfNull(values);

		Fields = fields;
		Values = values;
	}

	/// <summary>Applies this override to inherited settings.</summary>
	/// <param name="inherited">The inherited settings.</param>
	/// <returns>The effective settings.</returns>
	public BrowseViewSettings ApplyTo(BrowseViewSettings inherited)
	{
		ArgumentNullException.ThrowIfNull(inherited);

		if (Fields == ViewSettingsOverrideFields.None)
		{
			return inherited;
		}

		if (Fields == ViewSettingsOverrideFields.All)
		{
			return Values;
		}

		return new BrowseViewSettings(
			Fields.HasFlag(ViewSettingsOverrideFields.LayoutMode) ? Values.LayoutMode : inherited.LayoutMode,
			Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? Values.Columns : inherited.Columns,
			Fields.HasFlag(ViewSettingsOverrideFields.SortPropertyId) ? Values.SortPropertyId : inherited.SortPropertyId,
			Fields.HasFlag(ViewSettingsOverrideFields.SortDirection) ? Values.SortDirection : inherited.SortDirection,
			Fields.HasFlag(ViewSettingsOverrideFields.ItemSize) ? Values.ItemSize : inherited.ItemSize,
			Fields.HasFlag(ViewSettingsOverrideFields.GroupPropertyId) ? Values.GroupPropertyId : inherited.GroupPropertyId,
			Fields.HasFlag(ViewSettingsOverrideFields.GroupDirection) ? Values.GroupDirection : inherited.GroupDirection);
	}

	/// <summary>Merges a higher-priority override into this override.</summary>
	/// <param name="higherPriority">The override whose supplied fields take precedence.</param>
	/// <returns>The merged override.</returns>
	public BrowseViewSettingsOverride Merge(BrowseViewSettingsOverride higherPriority)
	{
		ArgumentNullException.ThrowIfNull(higherPriority);

		if (higherPriority.Fields == ViewSettingsOverrideFields.None)
		{
			return this;
		}

		if (Fields == ViewSettingsOverrideFields.None || higherPriority.Fields == ViewSettingsOverrideFields.All)
		{
			return higherPriority;
		}

		return new BrowseViewSettingsOverride(Fields | higherPriority.Fields, higherPriority.ApplyTo(Values));
	}

	/// <summary>Creates an override that supplies every setting.</summary>
	/// <param name="settings">The complete settings.</param>
	/// <returns>An override containing all settings.</returns>
	public static BrowseViewSettingsOverride FromSettings(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new BrowseViewSettingsOverride(ViewSettingsOverrideFields.All, settings);
	}
}

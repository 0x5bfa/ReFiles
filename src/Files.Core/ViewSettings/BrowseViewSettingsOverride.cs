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

	/// <summary>Gets how supplied column settings combine with inherited columns.</summary>
	public ViewColumnSettingsMode ColumnMode { get; }

	/// <summary>Initializes a partial browse view settings override.</summary>
	/// <param name="fields">The settings supplied by the override.</param>
	/// <param name="values">The values for supplied settings.</param>
	/// <param name="columnMode">How supplied column settings combine with inherited columns.</param>
	public BrowseViewSettingsOverride(ViewSettingsOverrideFields fields, BrowseViewSettings values, ViewColumnSettingsMode columnMode = ViewColumnSettingsMode.Replace)
	{
		if ((fields & ~ViewSettingsOverrideFields.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fields));
		}

		ArgumentNullException.ThrowIfNull(values);

		if (!Enum.IsDefined(columnMode))
		{
			throw new ArgumentOutOfRangeException(nameof(columnMode));
		}

		Fields = fields;
		Values = values;
		ColumnMode = columnMode;
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

		if (Fields == ViewSettingsOverrideFields.All && ColumnMode is ViewColumnSettingsMode.Replace)
		{
			return Values;
		}

		var columns = Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns)
			? ColumnMode is ViewColumnSettingsMode.Insert ? InsertColumns(inherited.Columns, Values.Columns) : Values.Columns
			: inherited.Columns;

		return new BrowseViewSettings(
			Fields.HasFlag(ViewSettingsOverrideFields.LayoutMode) ? Values.LayoutMode : inherited.LayoutMode,
			columns,
			Fields.HasFlag(ViewSettingsOverrideFields.SortPropertyId) ? Values.SortPropertyId : inherited.SortPropertyId,
			Fields.HasFlag(ViewSettingsOverrideFields.SortDirection) ? Values.SortDirection : inherited.SortDirection,
			Fields.HasFlag(ViewSettingsOverrideFields.ItemSize) ? Values.ItemSize : inherited.ItemSize,
			Fields.HasFlag(ViewSettingsOverrideFields.GroupPropertyId) ? Values.GroupPropertyId : inherited.GroupPropertyId,
			Fields.HasFlag(ViewSettingsOverrideFields.GroupDirection) ? Values.GroupDirection : inherited.GroupDirection);
	}

	/// <summary>Merges a higher-priority override into this override.</summary>
	/// <param name="higherPriority">The override whose supplied fields take precedence.</param>
	/// <returns>The merged override.</returns>
	/// <remarks>
	/// When both overrides insert columns, the higher-priority insertion replaces the lower-priority insertion because absolute positions require an inherited column catalog to compose.
	/// </remarks>
	public BrowseViewSettingsOverride Merge(BrowseViewSettingsOverride higherPriority)
	{
		ArgumentNullException.ThrowIfNull(higherPriority);

		if (higherPriority.Fields == ViewSettingsOverrideFields.None)
		{
			return this;
		}

		if (Fields == ViewSettingsOverrideFields.None)
		{
			return higherPriority;
		}

		var mergedValues = higherPriority.ApplyTo(Values);
		var higherInsertsColumns = higherPriority.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) && higherPriority.ColumnMode is ViewColumnSettingsMode.Insert;
		var lowerDoesNotReplaceColumns = !Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) || ColumnMode is ViewColumnSettingsMode.Insert;
		if (higherInsertsColumns && lowerDoesNotReplaceColumns)
		{
			mergedValues = new BrowseViewSettings(mergedValues.LayoutMode, higherPriority.Values.Columns, mergedValues.SortPropertyId, mergedValues.SortDirection,
				mergedValues.ItemSize, mergedValues.GroupPropertyId, mergedValues.GroupDirection);
		}

		var columnMode = GetMergedColumnMode(higherPriority);

		return new BrowseViewSettingsOverride(Fields | higherPriority.Fields, mergedValues, columnMode);
	}

	/// <summary>Replaces selected fields while preserving fields outside the mask.</summary>
	/// <param name="fields">The fields to replace or clear.</param>
	/// <param name="replacement">Replacement values whose supplied fields must be a subset of <paramref name="fields"/>.</param>
	/// <returns>The patched override.</returns>
	public BrowseViewSettingsOverride ReplaceFields(ViewSettingsOverrideFields fields, BrowseViewSettingsOverride replacement)
	{
		if ((fields & ~ViewSettingsOverrideFields.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fields));
		}

		ArgumentNullException.ThrowIfNull(replacement);

		if ((replacement.Fields & ~fields) != 0)
		{
			throw new ArgumentException("Replacement settings contain fields outside the replacement mask.", nameof(replacement));
		}

		var retainedFields = Fields & ~fields;
		var retainedColumnMode = retainedFields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? ColumnMode : ViewColumnSettingsMode.Replace;
		var retained = new BrowseViewSettingsOverride(retainedFields, Values, retainedColumnMode);

		return retained.Merge(replacement);
	}

	/// <summary>Creates an override that supplies every setting.</summary>
	/// <param name="settings">The complete settings.</param>
	/// <returns>An override containing all settings.</returns>
	public static BrowseViewSettingsOverride FromSettings(BrowseViewSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new BrowseViewSettingsOverride(ViewSettingsOverrideFields.All, settings);
	}

	private static IReadOnlyList<ViewColumnSettings> InsertColumns(IReadOnlyList<ViewColumnSettings> inherited, IReadOnlyList<ViewColumnSettings> inserted)
	{
		if (inserted.Count is 0)
		{
			return inherited;
		}

		var insertedPropertyIds = inserted.Select(static column => column.PropertyId).ToHashSet(StringComparer.Ordinal);
		var result = inherited.Where(column => !insertedPropertyIds.Contains(column.PropertyId)).OrderBy(static column => column.Order).ToList();
		foreach (var column in inserted.OrderBy(static column => column.Order))
		{
			result.Insert(Math.Min(column.Order, result.Count), column);
		}

		return Array.AsReadOnly(result.Select(static (column, index) => new ViewColumnSettings(column.PropertyId, column.Width, index, column.IsVisible)).ToArray());
	}

	private ViewColumnSettingsMode GetMergedColumnMode(BrowseViewSettingsOverride higherPriority)
	{
		if (!higherPriority.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns))
		{
			return ColumnMode;
		}

		return higherPriority.ColumnMode is ViewColumnSettingsMode.Replace || Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) && ColumnMode is ViewColumnSettingsMode.Replace
			? ViewColumnSettingsMode.Replace
			: ViewColumnSettingsMode.Insert;
	}
}

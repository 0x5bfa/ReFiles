// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Security.Cryptography;
using System.Text;
using Files.Core.ViewSettings;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal static class WindowsShellViewSettingsPersistence
{
	private const ViewSettingsOverrideFields SortFields = ViewSettingsOverrideFields.SortPropertyId | ViewSettingsOverrideFields.SortDirection;
	private const ViewSettingsOverrideFields GroupFields = ViewSettingsOverrideFields.GroupPropertyId | ViewSettingsOverrideFields.GroupDirection;
	private const string PersistenceMutexPrefix = @"Local\ReFiles.ViewSettings.";

	internal static BrowseViewSettingsOverride? Read(IShellItem shellItem, string parsingName, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		using var persistenceMutex = AcquirePersistenceMutex(parsingName, cancellationToken);
		try
		{
			return WindowsShellViewSettingsPropertyBag.Read(shellItem, cancellationToken);
		}
		finally
		{
			persistenceMutex.ReleaseMutex();
		}
	}

	internal static ViewSettingsPersistenceResult Write(IShellItem shellItem, string parsingName, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(settingsOverride);

		cancellationToken.ThrowIfCancellationRequested();

		var columnSet = WindowsShellColumnReader.Read(shellItem, parsingName, cancellationToken);
		var supportedPropertyIds = columnSet.All.Select(static column => column.PropertyId).ToHashSet(StringComparer.Ordinal);
		var providerFields = ViewSettingsOverrideFields.None;
		var applicationFields = ViewSettingsOverrideFields.None;
		IReadOnlyList<ViewColumnSettings> providerColumns = settingsOverride.Values.Columns;
		IReadOnlyList<ViewColumnSettings> applicationColumns = settingsOverride.Values.Columns;
		var applicationColumnMode = ViewColumnSettingsMode.Replace;

		PartitionLayout(settingsOverride, ref providerFields, ref applicationFields);
		PartitionColumns(settingsOverride, supportedPropertyIds, ref providerFields, ref applicationFields, ref providerColumns, ref applicationColumns, ref applicationColumnMode);
		PartitionPropertyPair(settingsOverride, SortFields, settingsOverride.Values.SortPropertyId, supportedPropertyIds, ref providerFields, ref applicationFields);
		PartitionPropertyPair(settingsOverride, GroupFields, settingsOverride.Values.GroupPropertyId, supportedPropertyIds, ref providerFields, ref applicationFields);
		applicationFields |= settingsOverride.Fields & ViewSettingsOverrideFields.ItemSize;

		var providerPatch = CreateOverride(providerFields, settingsOverride.Values, providerColumns);
		var applicationSettings = CreateOverride(applicationFields, settingsOverride.Values, applicationColumns, applicationColumnMode);
		var currentProviderSettings = WindowsShellViewSettingsPropertyBag.Read(shellItem, cancellationToken) ?? CreateOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default, []);
		if (providerPatch.Fields == ViewSettingsOverrideFields.None)
		{
			return new ViewSettingsPersistenceResult(currentProviderSettings, applicationSettings);
		}

		BrowseViewSettingsOverride nextProviderSettings;
		using var persistenceMutex = AcquirePersistenceMutex(parsingName, cancellationToken);
		try
		{
			currentProviderSettings = WindowsShellViewSettingsPropertyBag.Read(shellItem, cancellationToken) ?? CreateOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default, []);
			nextProviderSettings = currentProviderSettings.Merge(providerPatch);
			WindowsShellViewSettingsPropertyBag.Write(shellItem, nextProviderSettings, cancellationToken);
		}
		finally
		{
			persistenceMutex.ReleaseMutex();
		}

		return new ViewSettingsPersistenceResult(nextProviderSettings, applicationSettings);
	}

	internal static BrowseViewSettingsOverride Clear(IShellItem shellItem, string parsingName, ViewSettingsOverrideFields fields, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		if ((fields & ~ViewSettingsOverrideFields.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(fields));
		}

		cancellationToken.ThrowIfCancellationRequested();

		using var persistenceMutex = AcquirePersistenceMutex(parsingName, cancellationToken);
		try
		{
			var current = WindowsShellViewSettingsPropertyBag.Read(shellItem, cancellationToken);
			if (current is null || fields == ViewSettingsOverrideFields.None)
			{
				return current ?? CreateOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default, []);
			}

			var retainedFields = current.Fields & ~fields;
			var retainedColumnMode = retainedFields.HasFlag(ViewSettingsOverrideFields.DetailsColumns) ? current.ColumnMode : ViewColumnSettingsMode.Replace;
			var retained = new BrowseViewSettingsOverride(retainedFields, current.Values, retainedColumnMode);
			WindowsShellViewSettingsPropertyBag.Write(shellItem, retainedFields == ViewSettingsOverrideFields.None ? null : retained, cancellationToken);

			return retained;
		}
		finally
		{
			persistenceMutex.ReleaseMutex();
		}
	}

	private static void PartitionLayout(BrowseViewSettingsOverride settingsOverride, ref ViewSettingsOverrideFields providerFields, ref ViewSettingsOverrideFields applicationFields)
	{
		if (!settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.LayoutMode))
		{
			return;
		}

		if (settingsOverride.Values.LayoutMode is ViewLayoutMode.Columns)
		{
			applicationFields |= ViewSettingsOverrideFields.LayoutMode;
		}
		else
		{
			providerFields |= ViewSettingsOverrideFields.LayoutMode;
		}
	}

	private static void PartitionColumns(
		BrowseViewSettingsOverride settingsOverride,
		IReadOnlySet<string> supportedPropertyIds,
		ref ViewSettingsOverrideFields providerFields,
		ref ViewSettingsOverrideFields applicationFields,
		ref IReadOnlyList<ViewColumnSettings> providerColumns,
		ref IReadOnlyList<ViewColumnSettings> applicationColumns,
		ref ViewColumnSettingsMode applicationColumnMode)
	{
		if (!settingsOverride.Fields.HasFlag(ViewSettingsOverrideFields.DetailsColumns))
		{
			return;
		}

		var nativeColumns = NormalizeColumns(settingsOverride.Values.Columns.Where(column => supportedPropertyIds.Contains(column.PropertyId)));
		var customColumns = settingsOverride.Values.Columns.Where(column => !supportedPropertyIds.Contains(column.PropertyId)).OrderBy(static column => column.Order).ToArray();
		if (supportedPropertyIds.Count is 0 || nativeColumns.Count is 0)
		{
			applicationFields |= ViewSettingsOverrideFields.DetailsColumns;
			applicationColumnMode = settingsOverride.ColumnMode;

			return;
		}

		providerFields |= ViewSettingsOverrideFields.DetailsColumns;
		providerColumns = nativeColumns;
		if (customColumns.Length is not 0)
		{
			applicationFields |= ViewSettingsOverrideFields.DetailsColumns;
			applicationColumns = Array.AsReadOnly(customColumns);
			applicationColumnMode = ViewColumnSettingsMode.Insert;
		}
	}

	private static void PartitionPropertyPair(
		BrowseViewSettingsOverride settingsOverride,
		ViewSettingsOverrideFields pairFields,
		string? propertyId,
		IReadOnlySet<string> supportedPropertyIds,
		ref ViewSettingsOverrideFields providerFields,
		ref ViewSettingsOverrideFields applicationFields)
	{
		var requestedFields = settingsOverride.Fields & pairFields;
		if (requestedFields == ViewSettingsOverrideFields.None)
		{
			return;
		}

		if (propertyId is null || supportedPropertyIds.Contains(propertyId))
		{
			providerFields |= requestedFields;
		}
		else
		{
			applicationFields |= requestedFields;
		}
	}

	private static BrowseViewSettingsOverride CreateOverride(
		ViewSettingsOverrideFields fields,
		BrowseViewSettings values,
		IReadOnlyList<ViewColumnSettings> columns,
		ViewColumnSettingsMode columnMode = ViewColumnSettingsMode.Replace)
	{
		var overrideValues = new BrowseViewSettings(values.LayoutMode, columns, values.SortPropertyId, values.SortDirection, values.ItemSize, values.GroupPropertyId, values.GroupDirection);

		return new BrowseViewSettingsOverride(fields, overrideValues, columnMode);
	}

	private static IReadOnlyList<ViewColumnSettings> NormalizeColumns(IEnumerable<ViewColumnSettings> columns)
	{
		return Array.AsReadOnly(columns.OrderBy(static column => column.Order).Select(static (column, index) => new ViewColumnSettings(column.PropertyId, column.Width, index, column.IsVisible)).ToArray());
	}

	private static Mutex AcquirePersistenceMutex(string parsingName, CancellationToken cancellationToken)
	{
		var identityBytes = Encoding.UTF8.GetBytes(parsingName);
		var mutexName = $"{PersistenceMutexPrefix}{Convert.ToHexString(SHA256.HashData(identityBytes))}";
		var persistenceMutex = new Mutex(initiallyOwned: false, mutexName);
		try
		{
			try
			{
				if (cancellationToken.CanBeCanceled)
				{
					var signaledHandle = WaitHandle.WaitAny([persistenceMutex, cancellationToken.WaitHandle]);
					if (signaledHandle is not 0)
					{
						cancellationToken.ThrowIfCancellationRequested();

					}
				}
				else
				{
					persistenceMutex.WaitOne();
				}
			}
			catch (AbandonedMutexException)
			{
				// An abandoned mutex is acquired by the waiting thread.
			}

			return persistenceMutex;
		}
		catch
		{
			persistenceMutex.Dispose();

			throw;
		}
	}
}

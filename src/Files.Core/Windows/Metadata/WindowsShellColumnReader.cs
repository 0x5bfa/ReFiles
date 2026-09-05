// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Files.Core.Windows;

internal static unsafe class WindowsShellColumnReader
{
	private const int HeaderBufferLength = 256;

	private const uint MaximumColumnCount = 1024;

	private const string FolderTypesRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FolderTypes";

	private const string ColumnListValueName = "ColumnList";

	private static readonly IReadOnlyDictionary<string, int> _emptyColumnWidths = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));

	private static readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<string, int>> _folderTypeColumnWidths = new();

	internal static WindowsShellColumnSet Read(IShellItem shellItem, string parsingName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		cancellationToken.ThrowIfCancellationRequested();

		var folder = TryGetFolder2(shellItem, parsingName, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (folder is null)
		{
			return new WindowsShellColumnSet([], null, null);
		}

		var columns = new List<WindowsShellColumn>();
		HRESULT hr;
		for (uint index = 0; index < MaximumColumnCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var details = default(SHELLDETAILS);
			hr = folder.GetDetailsOf(null, index, &details);
			if (hr.Failed)
			{
				break;
			}

			hr = folder.MapColumnToSCID(index, out var propertyKey);
			if (hr.Failed)
			{
				continue;
			}

			var propertyId = GetPropertyId(propertyKey);
			var displayName = TryReadDisplayName(ref details.str, out var headerText) ? headerText : string.Empty;
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = propertyId;
			}

			var state = SHCOLSTATE.SHCOLSTATE_DEFAULT;
			hr = folder.GetDefaultColumnState(index, out state);
			if (hr.Failed)
			{
				state = SHCOLSTATE.SHCOLSTATE_DEFAULT;
			}

			columns.Add(new WindowsShellColumn(
				checked((int)index),
				propertyId,
				displayName,
				Math.Max(0, details.cxChar),
				GetAlignment(details.fmt),
				HasState(state, SHCOLSTATE.SHCOLSTATE_ONBYDEFAULT),
				HasState(state, SHCOLSTATE.SHCOLSTATE_HIDDEN),
				HasState(state, SHCOLSTATE.SHCOLSTATE_SLOW),
				HasState(state, SHCOLSTATE.SHCOLSTATE_EXTENDED),
				HasState(state, SHCOLSTATE.SHCOLSTATE_SECONDARYUI),
				!HasState(state, SHCOLSTATE.SHCOLSTATE_NO_GROUPBY),
				HasState(state, SHCOLSTATE.SHCOLSTATE_FIXED_WIDTH),
				HasState(state, SHCOLSTATE.SHCOLSTATE_PREFER_VARCMP),
				GetColumnType(state)));
		}

		var defaultSortColumnIndex = default(int?);
		var defaultDisplayColumnIndex = default(int?);
		hr = folder.GetDefaultColumn(0, out var sortColumnIndex, out var displayColumnIndex);
		if (hr.Succeeded)
		{
			defaultSortColumnIndex = ToColumnIndex(sortColumnIndex);
			defaultDisplayColumnIndex = ToColumnIndex(displayColumnIndex);
		}

		var shellColumnSet = new WindowsShellColumnSet(columns, defaultSortColumnIndex, defaultDisplayColumnIndex);
		cancellationToken.ThrowIfCancellationRequested();
		var isFileSystemFolder = shellItem.GetAttributes(SFGAO_FLAGS.SFGAO_FILESYSTEM, out var attributes).Succeeded && (attributes & SFGAO_FLAGS.SFGAO_FILESYSTEM) != 0;
		var defaultColumnWidths = isFileSystemFolder ? GetFolderTypeColumnWidths(parsingName) : _emptyColumnWidths;
		cancellationToken.ThrowIfCancellationRequested();

		if (TryReadViewColumns(folder, shellColumnSet, defaultColumnWidths, cancellationToken, out var viewColumnSet))
		{
			return viewColumnSet;
		}

		cancellationToken.ThrowIfCancellationRequested();

		return shellColumnSet;
	}

	private static bool TryReadViewColumns(
		IShellFolder folder,
		WindowsShellColumnSet shellColumnSet,
		IReadOnlyDictionary<string, int> defaultColumnWidths,
		CancellationToken cancellationToken,
		out WindowsShellColumnSet columnSet)
	{
		columnSet = shellColumnSet;
		cancellationToken.ThrowIfCancellationRequested();

		var hr = folder.CreateViewObject(HWND.Null, out IShellView? shellView);
		if (hr.Failed || shellView is not IColumnManager columnManager)
		{
			return false;
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (!TryGetColumnKeys(columnManager, CM_ENUM_FLAGS.CM_ENUM_ALL, cancellationToken, out var allKeys))
		{
			return false;
		}

		var visibleKeys = TryGetColumnKeys(columnManager, CM_ENUM_FLAGS.CM_ENUM_VISIBLE, cancellationToken, out var keys)
			? keys
			: [];
		var visiblePropertyIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in visibleKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			visiblePropertyIds.Add(GetPropertyId(key));
		}

		var allEntries = new List<(PROPERTYKEY Key, string PropertyId)>(allKeys.Length);
		var entriesByPropertyId = new Dictionary<string, (PROPERTYKEY Key, string PropertyId)>(StringComparer.Ordinal);
		foreach (var key in allKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var propertyId = GetPropertyId(key);
			if (entriesByPropertyId.ContainsKey(propertyId))
			{
				continue;
			}

			var entry = (key, propertyId);
			allEntries.Add(entry);
			entriesByPropertyId.Add(propertyId, entry);
		}

		var orderedEntries = new List<(PROPERTYKEY Key, string PropertyId)>(allEntries.Count);
		var orderedPropertyIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in visibleKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var propertyId = GetPropertyId(key);
			if (entriesByPropertyId.TryGetValue(propertyId, out var entry) && orderedPropertyIds.Add(propertyId))
			{
				orderedEntries.Add(entry);
			}
		}

		foreach (var entry in allEntries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (orderedPropertyIds.Add(entry.PropertyId))
			{
				orderedEntries.Add(entry);
			}
		}

		var legacyColumns = shellColumnSet.All.ToDictionary(static column => column.PropertyId, StringComparer.Ordinal);
		var columns = new List<WindowsShellColumn>(orderedEntries.Count);
		for (var index = 0; index < orderedEntries.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var entry = orderedEntries[index];
			CM_COLUMNINFO columnInfo = default;
			columnInfo.cbSize = checked((uint)Marshal.SizeOf<CM_COLUMNINFO>());
			columnInfo.dwMask = (uint)(CM_MASK.CM_MASK_WIDTH | CM_MASK.CM_MASK_DEFAULTWIDTH | CM_MASK.CM_MASK_IDEALWIDTH | CM_MASK.CM_MASK_NAME | CM_MASK.CM_MASK_STATE);
			hr = columnManager.GetColumnInfo(in entry.Key, ref columnInfo);
			cancellationToken.ThrowIfCancellationRequested();

			legacyColumns.TryGetValue(entry.PropertyId, out var legacyColumn);
			var displayName = hr.Succeeded ? columnInfo.wszName.ToString() : string.Empty;
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = legacyColumn?.DisplayName ?? entry.PropertyId;
			}

			var headerWidthCharacters = GetColumnWidthCharacters(entry.PropertyId, columnInfo, hr.Succeeded, legacyColumn, defaultColumnWidths);
			var isVisible = hr.Succeeded
				? HasColumnState(columnInfo.dwState, CM_STATE.CM_STATE_VISIBLE)
				: visiblePropertyIds.Contains(entry.PropertyId);
			columns.Add(new WindowsShellColumn(
				index,
				entry.PropertyId,
				displayName,
				headerWidthCharacters,
				legacyColumn?.Alignment ?? WindowsShellColumnAlignment.Left,
				isVisible,
				legacyColumn?.IsHidden is true,
				legacyColumn?.IsSlow is true,
				legacyColumn?.IsExtended is true,
				legacyColumn?.IsSecondaryUi is true,
				legacyColumn?.CanGroup is not false,
				hr.Succeeded ? HasColumnState(columnInfo.dwState, CM_STATE.CM_STATE_FIXEDWIDTH) : legacyColumn?.IsFixedWidth is true,
				legacyColumn?.PreferVariantCompare is true,
				legacyColumn?.Type ?? WindowsShellColumnType.Default));
		}

		var defaultSortColumnIndex = MapColumnIndex(shellColumnSet.DefaultSortColumnIndex, shellColumnSet.All, columns);
		var defaultDisplayColumnIndex = MapColumnIndex(shellColumnSet.DefaultDisplayColumnIndex, shellColumnSet.All, columns);
		columnSet = new WindowsShellColumnSet(columns, defaultSortColumnIndex, defaultDisplayColumnIndex);
		cancellationToken.ThrowIfCancellationRequested();

		return true;
	}

	private static bool TryGetColumnKeys(IColumnManager columnManager, CM_ENUM_FLAGS flags, CancellationToken cancellationToken, out PROPERTYKEY[] keys)
	{
		keys = [];
		cancellationToken.ThrowIfCancellationRequested();

		var hr = columnManager.GetColumnCount(flags, out var count);
		if (hr.Failed || count is 0 || count > MaximumColumnCount)
		{
			return false;
		}

		var length = checked((int)count);
		keys = new PROPERTYKEY[length];
		cancellationToken.ThrowIfCancellationRequested();

		hr = columnManager.GetColumns(flags, keys);
		if (hr.Succeeded)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return true;
		}

		keys = [];
		return false;
	}

	private static int GetColumnWidthCharacters(string propertyId, CM_COLUMNINFO columnInfo, bool hasColumnInfo, WindowsShellColumn? legacyColumn, IReadOnlyDictionary<string, int> defaultColumnWidths)
	{
		if (hasColumnInfo && columnInfo.uWidth is > 0 and < 4096)
		{
			return Math.Max(1, (int)Math.Round(columnInfo.uWidth / 8d));
		}

		if (hasColumnInfo && columnInfo.uDefaultWidth is > 0 and < 4096)
		{
			return Math.Max(1, (int)Math.Round(columnInfo.uDefaultWidth / 8d));
		}

		if (defaultColumnWidths.TryGetValue(propertyId, out var widthCharacters))
		{
			return widthCharacters;
		}

		return legacyColumn?.HeaderWidthCharacters is > 0 ? legacyColumn.HeaderWidthCharacters : 0;
	}

	private static IReadOnlyDictionary<string, int> GetFolderTypeColumnWidths(string parsingName)
	{
		var folderTypeId = GetFolderTypeId(parsingName);
		var widths = _folderTypeColumnWidths.GetOrAdd(folderTypeId, ReadFolderTypeColumnWidths);
		if (widths.Count is 0 && folderTypeId != PInvoke.FOLDERTYPEID_Generic)
		{
			return _folderTypeColumnWidths.GetOrAdd(PInvoke.FOLDERTYPEID_Generic, ReadFolderTypeColumnWidths);
		}

		return widths;
	}

	private static Guid GetFolderTypeId(string parsingName)
	{
		try
		{
			var manager = KnownFolderManager.CreateInstance<IKnownFolderManager>();
			if (manager.FindFolderFromPath(parsingName, FFFP_MODE.FFFP_EXACTMATCH, out var knownFolder).Succeeded && knownFolder.GetFolderType(out var folderTypeId).Succeeded)
			{
				return folderTypeId;
			}
		}
		catch (COMException)
		{
		}

		return PInvoke.FOLDERTYPEID_Generic;
	}

	private static IReadOnlyDictionary<string, int> ReadFolderTypeColumnWidths(Guid folderTypeId)
	{
		var widths = new Dictionary<string, int>(StringComparer.Ordinal);
		var topViewPath = $@"{FolderTypesRegistryPath}\{folderTypeId:B}\TopViews\{Guid.Empty:B}";
		using var topViewKey = Registry.LocalMachine.OpenSubKey(topViewPath);
		if (topViewKey?.GetValue(ColumnListValueName) is not string columnList || string.IsNullOrWhiteSpace(columnList))
		{
			return widths;
		}

		var interfaceId = typeof(IPropertyDescriptionList).GUID;
		var hr = PInvoke.PSGetPropertyDescriptionListFromString(columnList, in interfaceId, out var descriptions);
		if (hr.Failed || descriptions is null)
		{
			return widths;
		}

		hr = descriptions.GetCount(out var count);
		if (hr.Failed || count > MaximumColumnCount)
		{
			return widths;
		}

		for (var index = 0u; index < count; index++)
		{
			hr = descriptions.GetAt<IPropertyDescription>(index, out var description);
			if (hr.Failed || description is null)
			{
				continue;
			}

			hr = description.GetPropertyKey(out var propertyKey);
			if (hr.Failed)
			{
				continue;
			}

			hr = description.GetDefaultColumnWidth(out var widthCharacters);
			if (hr.Failed || widthCharacters is 0 or >= 4096)
			{
				continue;
			}

			widths[GetPropertyId(propertyKey)] = checked((int)widthCharacters);
		}

		return widths;
	}

	private static int? MapColumnIndex(int? sourceIndex, IReadOnlyList<WindowsShellColumn> sourceColumns, IReadOnlyList<WindowsShellColumn> targetColumns)
	{
		if (sourceIndex is null || sourceIndex.Value < 0 || sourceIndex.Value >= sourceColumns.Count)
		{
			return null;
		}

		var propertyId = sourceColumns[sourceIndex.Value].PropertyId;
		for (var index = 0; index < targetColumns.Count; index++)
		{
			if (targetColumns[index].PropertyId.Equals(propertyId, StringComparison.Ordinal))
			{
				return index;
			}
		}

		return null;
	}

	internal static unsafe WindowsShellPropertyDetails ReadPropertyDetails(IShellItem shellItem, IReadOnlyList<string> propertyIds, bool includeFormattedValues, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		ArgumentNullException.ThrowIfNull(propertyIds);

		var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal);
		var displayValues = new Dictionary<string, string>(StringComparer.Ordinal);
		if (propertyIds.Count is 0)
		{
			return new WindowsShellPropertyDetails(new ReadOnlyDictionary<string, object?>(rawValues), new ReadOnlyDictionary<string, string>(displayValues));
		}

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		var hr = PInvoke.SHGetIDListFromObject(shellItem, out absolutePidl);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return new WindowsShellPropertyDetails(new ReadOnlyDictionary<string, object?>(rawValues), new ReadOnlyDictionary<string, string>(displayValues));
		}

		try
		{
			var shellFolderId = typeof(IShellFolder).GUID;
			hr = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
			if (hr.Failed || parentObject is not IShellFolder parentFolder || childPidl is null)
			{
				return new WindowsShellPropertyDetails(new ReadOnlyDictionary<string, object?>(rawValues), new ReadOnlyDictionary<string, string>(displayValues));
			}

			if (parentFolder is not IShellFolder2 parentFolder2)
			{
				return new WindowsShellPropertyDetails(new ReadOnlyDictionary<string, object?>(rawValues), new ReadOnlyDictionary<string, string>(displayValues));
			}

			var propertyKeys = ResolvePropertyKeys(propertyIds, cancellationToken);
			var displayColumns = includeFormattedValues ? ResolveDisplayColumns(parentFolder2, propertyIds, cancellationToken) : null;

			return ReadPropertyDetailsCore(parentFolder2, in *childPidl, propertyKeys, displayColumns, cancellationToken);
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	internal static IReadOnlyList<WindowsShellPropertyDetails> ReadPropertyDetails(
		IShellFolder2 parentFolder,
		IReadOnlyList<ReadOnlyMemory<byte>> relativePidls,
		IReadOnlyList<string> propertyIds,
		bool includeFormattedValues,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(parentFolder);
		ArgumentNullException.ThrowIfNull(relativePidls);
		ArgumentNullException.ThrowIfNull(propertyIds);

		var propertyKeys = ResolvePropertyKeys(propertyIds, cancellationToken);
		var displayColumns = includeFormattedValues ? ResolveDisplayColumns(parentFolder, propertyIds, cancellationToken) : null;
		var results = new WindowsShellPropertyDetails[relativePidls.Count];
		for (var index = 0; index < relativePidls.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var relativePidl = relativePidls[index];
			if (relativePidl.IsEmpty)
			{
				results[index] = CreateEmptyPropertyDetails();

				continue;
			}

			fixed (byte* relativePidlBytes = relativePidl.Span)
			{
				results[index] = ReadPropertyDetailsCore(parentFolder, in *(ITEMIDLIST*)relativePidlBytes, propertyKeys, displayColumns, cancellationToken);
			}
		}

		return Array.AsReadOnly(results);
	}

	internal static IShellFolder2? TryGetFolder(IShellItem shellItem, string parsingName, CancellationToken cancellationToken)
	{
		return TryGetFolder2(shellItem, parsingName, cancellationToken);
	}

	internal static int? FindColumnIndex(IShellFolder2 folder, string propertyId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(folder);

		ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

		var columns = ResolveDisplayColumns(folder, [propertyId], cancellationToken);

		return columns.TryGetValue(propertyId, out var index) && index <= int.MaxValue ? checked((int)index) : null;
	}

	private static WindowsShellPropertyDetails CreateEmptyPropertyDetails()
	{
		return new WindowsShellPropertyDetails(
			new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal)),
			new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)));
	}

	private static WindowsShellPropertyDetails ReadPropertyDetailsCore(
		IShellFolder2 parentFolder,
		in ITEMIDLIST childPidl,
		IReadOnlyList<(string PropertyId, PROPERTYKEY Key)> propertyKeys,
		IReadOnlyDictionary<string, uint>? displayColumns,
		CancellationToken cancellationToken)
	{
		var rawValues = new Dictionary<string, object?>(StringComparer.Ordinal);
		var displayValues = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var property in propertyKeys)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ComVariant variant = default;
			try
			{
				var hr = parentFolder.GetDetailsEx(in childPidl, in property.Key, out variant);
				if (hr.Succeeded)
				{
					rawValues[property.PropertyId] = ReadVariantValue(variant);
				}
			}
			finally
			{
				variant.Dispose();
			}
		}

		if (displayColumns is not null)
		{
			foreach (var column in displayColumns)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var hr = parentFolder.GetDetailsOf(in childPidl, column.Value, out var itemDetails);
				if (hr.Succeeded && TryReadDisplayName(ref itemDetails.str, in childPidl, out var displayText))
				{
					displayValues[column.Key] = displayText;
				}
			}
		}

		return new WindowsShellPropertyDetails(new ReadOnlyDictionary<string, object?>(rawValues), new ReadOnlyDictionary<string, string>(displayValues));
	}

	private static IReadOnlyList<(string PropertyId, PROPERTYKEY Key)> ResolvePropertyKeys(IReadOnlyList<string> propertyIds, CancellationToken cancellationToken)
	{
		var propertyKeys = new List<(string PropertyId, PROPERTYKEY Key)>(propertyIds.Count);
		foreach (var propertyId in propertyIds)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (TryGetPropertyKey(propertyId, out var propertyKey))
			{
				propertyKeys.Add((propertyId, propertyKey));
			}
		}

		return propertyKeys;
	}

	private static unsafe IReadOnlyDictionary<string, uint> ResolveDisplayColumns(IShellFolder2 parentFolder, IReadOnlyList<string> propertyIds, CancellationToken cancellationToken)
	{
		var remainingPropertyIds = new HashSet<string>(propertyIds, StringComparer.Ordinal);
		var displayColumns = new Dictionary<string, uint>(StringComparer.Ordinal);
		for (uint index = 0; index < MaximumColumnCount && remainingPropertyIds.Count is not 0; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var headerDetails = default(SHELLDETAILS);
			var hr = parentFolder.GetDetailsOf(null, index, &headerDetails);
			if (hr.Failed)
			{
				break;
			}

			TryReadDisplayName(ref headerDetails.str, out _);
			hr = parentFolder.MapColumnToSCID(index, out var propertyKey);
			if (hr.Failed)
			{
				continue;
			}

			var propertyId = GetPropertyId(propertyKey);
			if (remainingPropertyIds.Remove(propertyId))
			{
				displayColumns[propertyId] = index;
			}
		}

		return new ReadOnlyDictionary<string, uint>(displayColumns);
	}

	private static IShellFolder2? TryGetFolder2(IShellItem shellItem, string parsingName, CancellationToken cancellationToken)
	{
		var hr = shellItem.BindToHandler(null, PInvoke.BHID_SFObject, out IShellFolder? directFolder);
		if (hr.Succeeded && directFolder is IShellFolder2 directFolder2)
		{
			return directFolder2;
		}

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		hr = PInvoke.SHParseDisplayName(parsingName, null, out absolutePidl, 0, out _);
		if (hr.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return null;
		}

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			var shellFolderId = typeof(IShellFolder).GUID;
			hr = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
			cancellationToken.ThrowIfCancellationRequested();

			if (hr.Failed || parentObject is not IShellFolder parentFolder || childPidl is null)
			{
				return null;
			}

			hr = parentFolder.BindToObject(in *childPidl, null, out IShellFolder? folder);
			if (hr.Failed || folder is not IShellFolder2 folder2)
			{
				return null;
			}

			return folder2;
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}
	}

	private static bool TryReadDisplayName(ref STRRET displayName, out string value)
	{
		Span<char> buffer = stackalloc char[HeaderBufferLength];
		var hr = PInvoke.StrRetToBuf(ref displayName, null, buffer);

		return TryCreateDisplayName(hr, buffer, out value);
	}

	private static bool TryReadDisplayName(ref STRRET displayName, in ITEMIDLIST pidl, out string value)
	{
		Span<char> buffer = stackalloc char[HeaderBufferLength];
		HRESULT hr;
		fixed (STRRET* displayNamePointer = &displayName)
		fixed (ITEMIDLIST* pidlPointer = &pidl)
		fixed (char* bufferPointer = buffer)
		{
			hr = PInvoke.StrRetToBuf(displayNamePointer, pidlPointer, bufferPointer, checked((uint)buffer.Length));
		}

		return TryCreateDisplayName(hr, buffer, out value);
	}

	private static bool TryCreateDisplayName(HRESULT hr, Span<char> buffer, out string value)
	{
		if (hr.Failed)
		{
			value = string.Empty;

			return false;
		}

		var terminatorIndex = buffer.IndexOf('\0');
		if (terminatorIndex >= 0)
		{
			buffer = buffer[..terminatorIndex];
		}

		value = buffer.ToString();

		return true;
	}

	private static string GetPropertyId(PROPERTYKEY propertyKey)
	{
		var hr = PInvoke.PSGetNameFromPropertyKey(propertyKey, out var nativeName);
		if (hr.Succeeded)
		{
			try
			{
				var name = nativeName.ToString();
				if (!string.IsNullOrWhiteSpace(name))
				{
					return name;
				}
			}
			finally
			{
				PInvoke.CoTaskMemFree(nativeName.Value);
			}
		}

		return $"shell:{propertyKey.fmtid:D}:{propertyKey.pid}";
	}

	private static bool TryGetPropertyKey(string propertyId, out PROPERTYKEY propertyKey)
	{
		var hr = PInvoke.PSGetPropertyKeyFromName(propertyId, out propertyKey);
		if (hr.Succeeded)
		{
			return true;
		}

		const string fallbackPrefix = "shell:";
		if (!propertyId.StartsWith(fallbackPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var separatorIndex = propertyId.LastIndexOf(':');
		if (separatorIndex <= fallbackPrefix.Length || separatorIndex == propertyId.Length - 1)
		{
			return false;
		}

		if (!Guid.TryParse(propertyId[fallbackPrefix.Length..separatorIndex], out var formatId) || !uint.TryParse(propertyId[(separatorIndex + 1)..], out var propertyIdValue))
		{
			return false;
		}

		propertyKey = default;
		propertyKey.fmtid = formatId;
		propertyKey.pid = propertyIdValue;

		return true;
	}

	private static object? ReadVariantValue(ComVariant variant)
	{
		try
		{
			return variant.VarType switch
			{
				VarEnum.VT_EMPTY or VarEnum.VT_NULL => null,
				VarEnum.VT_I1 => variant.As<sbyte>(),
				VarEnum.VT_UI1 => variant.As<byte>(),
				VarEnum.VT_I2 => variant.As<short>(),
				VarEnum.VT_UI2 => variant.As<ushort>(),
				VarEnum.VT_I4 => variant.As<int>(),
				VarEnum.VT_UI4 => variant.As<uint>(),
				VarEnum.VT_I8 => variant.As<long>(),
				VarEnum.VT_UI8 => variant.As<ulong>(),
				VarEnum.VT_R4 => variant.As<float>(),
				VarEnum.VT_R8 => variant.As<double>(),
				VarEnum.VT_BOOL => variant.As<bool>(),
				VarEnum.VT_DATE => variant.As<DateTime>(),
				VarEnum.VT_BSTR or VarEnum.VT_LPSTR or VarEnum.VT_LPWSTR => variant.As<string>(),
				VarEnum.VT_DECIMAL => variant.As<decimal>(),
				VarEnum.VT_CLSID => variant.As<Guid>(),
				_ => null,
			};
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static WindowsShellColumnAlignment GetAlignment(int format)
	{
		return (format & 0x3) switch
		{
			1 => WindowsShellColumnAlignment.Right,
			2 => WindowsShellColumnAlignment.Center,
			_ => WindowsShellColumnAlignment.Left,
		};
	}

	private static WindowsShellColumnType GetColumnType(SHCOLSTATE state)
	{
		return ((uint)state & (uint)SHCOLSTATE.SHCOLSTATE_TYPEMASK) switch
		{
			(uint)SHCOLSTATE.SHCOLSTATE_TYPE_STR => WindowsShellColumnType.String,
			(uint)SHCOLSTATE.SHCOLSTATE_TYPE_INT => WindowsShellColumnType.Integer,
			(uint)SHCOLSTATE.SHCOLSTATE_TYPE_DATE => WindowsShellColumnType.DateTime,
			_ => WindowsShellColumnType.Default,
		};
	}

	private static bool HasState(SHCOLSTATE state, SHCOLSTATE flag)
	{
		return ((uint)state & (uint)flag) != 0;
	}

	private static bool HasColumnState(uint state, CM_STATE flag)
	{
		return (state & (uint)flag) != 0;
	}

	private static int? ToColumnIndex(uint index)
	{
		if (index is uint.MaxValue || index > int.MaxValue)
		{
			return null;
		}

		return checked((int)index);
	}
}

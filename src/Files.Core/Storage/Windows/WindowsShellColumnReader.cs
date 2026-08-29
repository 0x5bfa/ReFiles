// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsShellColumnReader
{
	private const int HeaderBufferLength = 256;

	private const uint MaximumColumnCount = 1024;

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
		for (uint index = 0; index < MaximumColumnCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var details = default(SHELLDETAILS);
			var detailsResult = folder.GetDetailsOf(null, index, &details);
			if (detailsResult.Failed)
			{
				break;
			}

			var mapResult = folder.MapColumnToSCID(index, out var propertyKey);
			if (mapResult.Failed)
			{
				continue;
			}

			var propertyId = GetPropertyId(propertyKey);
			var displayName = TryReadDisplayName(&details.str, null, out var headerText) ? headerText : string.Empty;
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = propertyId;
			}

			var state = SHCOLSTATE.SHCOLSTATE_DEFAULT;
			var stateResult = folder.GetDefaultColumnState(index, out state);
			if (stateResult.Failed)
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
		var defaultColumnResult = folder.GetDefaultColumn(0, out var sortColumnIndex, out var displayColumnIndex);
		if (defaultColumnResult.Succeeded)
		{
			defaultSortColumnIndex = ToColumnIndex(sortColumnIndex);
			defaultDisplayColumnIndex = ToColumnIndex(displayColumnIndex);
		}

		var shellColumnSet = new WindowsShellColumnSet(columns, defaultSortColumnIndex, defaultDisplayColumnIndex);
		cancellationToken.ThrowIfCancellationRequested();

		if (TryReadViewColumns(folder, shellColumnSet, cancellationToken, out var viewColumnSet))
		{
			return viewColumnSet;
		}

		cancellationToken.ThrowIfCancellationRequested();

		return shellColumnSet;
	}

	private static bool TryReadViewColumns(IShellFolder folder, WindowsShellColumnSet shellColumnSet, CancellationToken cancellationToken, out WindowsShellColumnSet columnSet)
	{
		columnSet = shellColumnSet;
		cancellationToken.ThrowIfCancellationRequested();

		var createViewResult = folder.CreateViewObject(HWND.Null, out IShellView? shellView);
		if (createViewResult.Failed || shellView is not IColumnManager columnManager)
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
			var columnInfo = new CM_COLUMNINFO
			{
				cbSize = checked((uint)Marshal.SizeOf<CM_COLUMNINFO>()),
				dwMask = (uint)(CM_MASK.CM_MASK_WIDTH | CM_MASK.CM_MASK_DEFAULTWIDTH | CM_MASK.CM_MASK_IDEALWIDTH | CM_MASK.CM_MASK_NAME | CM_MASK.CM_MASK_STATE),
			};
			var infoResult = columnManager.GetColumnInfo(in entry.Key, ref columnInfo);
			cancellationToken.ThrowIfCancellationRequested();

			legacyColumns.TryGetValue(entry.PropertyId, out var legacyColumn);
			var displayName = infoResult.Succeeded ? columnInfo.wszName.ToString() : string.Empty;
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = legacyColumn?.DisplayName ?? entry.PropertyId;
			}

			var headerWidthCharacters = GetColumnWidthCharacters(columnInfo, infoResult.Succeeded, legacyColumn);
			var isVisible = infoResult.Succeeded
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
				infoResult.Succeeded ? HasColumnState(columnInfo.dwState, CM_STATE.CM_STATE_FIXEDWIDTH) : legacyColumn?.IsFixedWidth is true,
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

		var countResult = columnManager.GetColumnCount(flags, out var count);
		if (countResult.Failed || count is 0 || count > MaximumColumnCount)
		{
			return false;
		}

		var length = checked((int)count);
		keys = new PROPERTYKEY[length];
		cancellationToken.ThrowIfCancellationRequested();

		var columnsResult = columnManager.GetColumns(flags, keys);
		if (columnsResult.Succeeded)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return true;
		}

		keys = [];
		return false;
	}

	private static int GetColumnWidthCharacters(CM_COLUMNINFO columnInfo, bool hasColumnInfo, WindowsShellColumn? legacyColumn)
	{
		if (hasColumnInfo && columnInfo.uWidth is > 0 and < 4096)
		{
			return Math.Max(1, (int)Math.Round(columnInfo.uWidth / 8d));
		}

		return legacyColumn?.HeaderWidthCharacters is > 0 ? legacyColumn.HeaderWidthCharacters : 0;
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

	internal static unsafe IReadOnlyDictionary<string, object?> ReadValues(string parsingName, IReadOnlyList<string> propertyIds, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(propertyIds);

		var values = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (propertyIds.Count is 0)
		{
			return new ReadOnlyDictionary<string, object?>(values);
		}

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		var parseResult = PInvoke.SHParseDisplayName(parsingName, null, out absolutePidl, 0, out _);
		if (parseResult.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return new ReadOnlyDictionary<string, object?>(values);
		}

		try
		{
			var shellFolderId = typeof(IShellFolder).GUID;
			var parentBindResult = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
			if (parentBindResult.Failed || parentObject is not IShellFolder parentFolder || childPidl is null)
			{
				return new ReadOnlyDictionary<string, object?>(values);
			}

			if (parentFolder is not IShellFolder2 parentFolder2)
			{
				return new ReadOnlyDictionary<string, object?>(values);
			}

			foreach (var propertyId in propertyIds)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!TryGetPropertyKey(propertyId, out var propertyKey))
				{
					continue;
				}

				ComVariant variant = default;
				try
				{
					var result = parentFolder2.GetDetailsEx(in *childPidl, in propertyKey, out variant);
					if (result.Failed)
					{
						continue;
					}

					values[propertyId] = ReadVariantValue(variant);
				}
				finally
				{
					variant.Dispose();
				}
			}
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}

		return new ReadOnlyDictionary<string, object?>(values);
	}

	internal static unsafe IReadOnlyDictionary<string, string> ReadDisplayValues(string parsingName, IReadOnlyList<string> propertyIds, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);
		ArgumentNullException.ThrowIfNull(propertyIds);

		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		var remainingPropertyIds = new HashSet<string>(propertyIds, StringComparer.Ordinal);
		if (remainingPropertyIds.Count is 0)
		{
			return new ReadOnlyDictionary<string, string>(values);
		}

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		var parseResult = PInvoke.SHParseDisplayName(parsingName, null, out absolutePidl, 0, out _);
		if (parseResult.Failed || absolutePidl is null)
		{
			if (absolutePidl is not null)
			{
				PInvoke.CoTaskMemFree(absolutePidl);
			}

			return new ReadOnlyDictionary<string, string>(values);
		}

		try
		{
			var shellFolderId = typeof(IShellFolder).GUID;
			var parentBindResult = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
			if (parentBindResult.Failed || parentObject is not IShellFolder2 parentFolder || childPidl is null)
			{
				return new ReadOnlyDictionary<string, string>(values);
			}

			for (uint index = 0; index < MaximumColumnCount && remainingPropertyIds.Count is not 0; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var headerDetails = default(SHELLDETAILS);
				var headerResult = parentFolder.GetDetailsOf(null, index, &headerDetails);
				if (headerResult.Failed)
				{
					break;
				}

				TryReadDisplayName(&headerDetails.str, null, out _);
				if (parentFolder.MapColumnToSCID(index, out var propertyKey).Failed)
				{
					continue;
				}

				var propertyId = GetPropertyId(propertyKey);
				if (!remainingPropertyIds.Remove(propertyId))
				{
					continue;
				}

				var itemDetails = default(SHELLDETAILS);
				var itemResult = parentFolder.GetDetailsOf(childPidl, index, &itemDetails);
				if (itemResult.Succeeded && TryReadDisplayName(&itemDetails.str, childPidl, out var displayText))
				{
					values[propertyId] = displayText;
				}
			}
		}
		finally
		{
			PInvoke.CoTaskMemFree(absolutePidl);
		}

		return new ReadOnlyDictionary<string, string>(values);
	}

	private static IShellFolder2? TryGetFolder2(IShellItem shellItem, string parsingName, CancellationToken cancellationToken)
	{
		var directBindResult = shellItem.BindToHandler(null, PInvoke.BHID_SFObject, out IShellFolder? directFolder);
		if (directBindResult.Succeeded && directFolder is IShellFolder2 directFolder2)
		{
			return directFolder2;
		}

		cancellationToken.ThrowIfCancellationRequested();

		ITEMIDLIST* absolutePidl = null;
		var parseResult = PInvoke.SHParseDisplayName(parsingName, null, out absolutePidl, 0, out _);
		if (parseResult.Failed || absolutePidl is null)
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
			var parentBindResult = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
			cancellationToken.ThrowIfCancellationRequested();

			if (parentBindResult.Failed || parentObject is not IShellFolder parentFolder || childPidl is null)
			{
				return null;
			}

			var folderBindResult = parentFolder.BindToObject(in *childPidl, null, out IShellFolder? folder);
			if (folderBindResult.Failed || folder is not IShellFolder2 folder2)
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

	private static bool TryReadDisplayName(STRRET* displayName, ITEMIDLIST* pidl, out string value)
	{
		Span<char> buffer = stackalloc char[HeaderBufferLength];
		HRESULT result;
		fixed (char* bufferPointer = buffer)
		{
			result = PInvoke.StrRetToBuf(displayName, pidl, new PWSTR(bufferPointer), checked((uint)buffer.Length));
		}

		if (result.Failed)
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
		var nameResult = PInvoke.PSGetNameFromPropertyKey(propertyKey, out var nativeName);
		if (nameResult.Succeeded)
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
		var result = PInvoke.PSGetPropertyKeyFromName(propertyId, out propertyKey);
		if (result.Succeeded)
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

		propertyKey = new PROPERTYKEY
		{
			fmtid = formatId,
			pid = propertyIdValue,
		};

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

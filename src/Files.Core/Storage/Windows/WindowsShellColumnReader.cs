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

	internal static WindowsShellColumnSet Read(IShellItem shellItem, string parsingName)
	{
		ArgumentNullException.ThrowIfNull(shellItem);

		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		var folder = TryGetFolder2(shellItem, parsingName);
		if (folder is null)
		{
			return new WindowsShellColumnSet([], null, null);
		}

		var columns = new List<WindowsShellColumn>();
		for (uint index = 0; index < MaximumColumnCount; index++)
		{
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
			var displayName = ReadDisplayName(ref details.str);
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

		return new WindowsShellColumnSet(columns, defaultSortColumnIndex, defaultDisplayColumnIndex);
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

	private static IShellFolder2? TryGetFolder2(IShellItem shellItem, string parsingName)
	{
		var directBindResult = shellItem.BindToHandler(null, PInvoke.BHID_SFObject, out IShellFolder? directFolder);
		if (directBindResult.Succeeded && directFolder is IShellFolder2 directFolder2)
		{
			return directFolder2;
		}

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
			var shellFolderId = typeof(IShellFolder).GUID;
			var parentBindResult = PInvoke.SHBindToParent(in *absolutePidl, in shellFolderId, out object parentObject, out ITEMIDLIST* childPidl);
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

	private static string ReadDisplayName(ref STRRET displayName)
	{
		Span<char> buffer = stackalloc char[HeaderBufferLength];
		var result = PInvoke.StrRetToBuf(ref displayName, null, buffer);
		if (result.Failed)
		{
			return string.Empty;
		}

		var terminatorIndex = buffer.IndexOf('\0');
		if (terminatorIndex >= 0)
		{
			buffer = buffer[..terminatorIndex];
		}

		return buffer.ToString();
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

	private static int? ToColumnIndex(uint index)
	{
		if (index is uint.MaxValue || index > int.MaxValue)
		{
			return null;
		}

		return checked((int)index);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Files.Core.Diagnostics;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Properties;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Reads Windows Shell properties for filesystem items.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsPropertyReader : IPropertyReader
{
	private const double MinimumOaDate = -657435d;
	private const double MaximumOaDate = 2958466d;
	private const string ItemTypeText = "System.ItemTypeText";
	private const string Size = "System.Size";
	private const string DateModified = "System.DateModified";
	private const string DateCreated = "System.DateCreated";
	private const string HomeIsPinned = "System.Home.IsPinned";

	private static readonly ulong _maximumFileTime = checked((ulong)DateTime.MaxValue.ToFileTime());
	private static readonly PROPERTYKEY _itemTypeTextKey = ResolvePropertyKey(ItemTypeText);
	private static readonly PROPERTYKEY _sizeKey = ResolvePropertyKey(Size);
	private static readonly PROPERTYKEY _dateModifiedKey = ResolvePropertyKey(DateModified);
	private static readonly PROPERTYKEY _dateCreatedKey = ResolvePropertyKey(DateCreated);
	private static readonly PROPERTYKEY _homeIsPinnedKey = new()
	{
		fmtid = new Guid(0x30C8EEF4u, 0xA832, 0x41E2, 0xAB, 0x32, 0xE3, 0xC3, 0xCA, 0x28, 0xFD, 0x29),
		pid = 4,
	};

	/// <summary>Determines whether this reader can read the item context.</summary>
	/// <param name="context">The item context.</param>
	/// <returns><see langword="true"/> when the context belongs to Windows storage.</returns>
	public bool CanRead(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource
			&& context.CoreModel is WindowsStorable;
	}

	/// <summary>Reads Windows Shell properties for a batch of items.</summary>
	/// <param name="request">The requested properties.</param>
	/// <param name="contexts">The item contexts to read.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>Properties grouped by item reference.</returns>
	public async ValueTask<IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>> GetPropertiesAsync(
		PropertyRequest request,
		IReadOnlyList<ItemContext> contexts,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(contexts);

		var startTimestamp = Stopwatch.GetTimestamp();
		CoreDiagnosticLog.Write("WindowsPropertyReader", $"GetProperties START propertyCount={request.PropertyIds.Count} contextCount={contexts.Count}");

		var readableContexts = contexts.Where(CanRead).ToArray();

		if (readableContexts.Length is 0)
		{
			CoreDiagnosticLog.Write("WindowsPropertyReader", $"GetProperties END readableContexts=0 elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			return EmptyResults.Instance;
		}

		var parentGroups = new Dictionary<WindowsItemLocator, List<ItemContext>>(ReferenceEqualityComparer.Instance);
		var tasks = new List<Task<PropertyEntry[]>>();
		foreach (var context in readableContexts)
		{
			var item = (WindowsStorable)context.CoreModel;
			if (item.Locator.ParentFolder is not { } parentFolder || item.Locator.RelativePidl.IsEmpty)
			{
				tasks.Add(ReadOneAsync(request, context, cancellationToken));

				continue;
			}

			if (!parentGroups.TryGetValue(parentFolder, out var group))
			{
				group = [];
				parentGroups.Add(parentFolder, group);
			}

			group.Add(context);
		}

		foreach (var group in parentGroups)
		{
			tasks.Add(ReadGroupAsync(request, group.Key, group.Value, cancellationToken));
		}

		var entries = (await Task.WhenAll(tasks).ConfigureAwait(false)).SelectMany(static group => group).ToArray();

		var results = entries.ToDictionary(static entry => entry.Reference, static entry => entry.Properties);
		CoreDiagnosticLog.Write(
			"WindowsPropertyReader",
			$"GetProperties END readableContexts={readableContexts.Length} parentGroups={parentGroups.Count} resultCount={results.Count} " +
			$"elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

		return new ReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>(results);
	}

	private static Task<PropertyEntry[]> ReadOneAsync(PropertyRequest request, ItemContext context, CancellationToken cancellationToken)
	{
		var source = (WindowsStorageSource)context.Source;
		var item = (WindowsStorable)context.CoreModel;

		return source.ShellItemResolver.InvokeConcurrentAsync(
			item.Locator,
			shellItem => new[] { new PropertyEntry(context.Reference, ReadPropertiesCore(shellItem, request, cancellationToken)) },
			cancellationToken);
	}

	private static Task<PropertyEntry[]> ReadGroupAsync(PropertyRequest request, WindowsItemLocator parentFolder, IReadOnlyList<ItemContext> contexts, CancellationToken cancellationToken)
	{
		var source = (WindowsStorageSource)contexts[0].Source;

		return source.ShellItemResolver.InvokeConcurrentAsync(
			parentFolder,
			shellItem => ReadGroupOnCurrentSta(shellItem, request, contexts, cancellationToken),
			cancellationToken);
	}

	private static PropertyEntry[] ReadGroupOnCurrentSta(IShellItem parentShellItem, PropertyRequest request, IReadOnlyList<ItemContext> contexts, CancellationToken cancellationToken)
	{
		var parentFolder = WindowsShellColumnReader.TryGetFolder(parentShellItem, ((WindowsStorable)contexts[0].CoreModel).Locator.ParentFolder!.ParsingName, cancellationToken);
		if (parentFolder is null)
		{
			return contexts.Select(context => ReadOneOnCurrentSta(request, context, cancellationToken)).ToArray();
		}

		var relativePidls = contexts.Select(static context => ((WindowsStorable)context.CoreModel).Locator.RelativePidl).ToArray();
		var details = WindowsShellColumnReader.ReadPropertyDetails(parentFolder, relativePidls, request.PropertyIds, request.IncludeFormattedValues, cancellationToken);
		var entries = new PropertyEntry[contexts.Count];
		for (var index = 0; index < contexts.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var context = contexts[index];
			var item = (WindowsStorable)context.CoreModel;
			var shellItem = HasAllProperties(details[index], request) ? null : item.Locator.ItemStoreReference?.TryGetItem(parentFolder) ?? WindowsShellItemResolver.TryCreateFromPidl(item.Locator.AbsolutePidl);
			entries[index] = new PropertyEntry(context.Reference, ReadPropertiesCore(shellItem, request, details[index], cancellationToken));
		}

		return entries;
	}

	private static PropertyEntry ReadOneOnCurrentSta(PropertyRequest request, ItemContext context, CancellationToken cancellationToken)
	{
		var item = (WindowsStorable)context.CoreModel;
		var shellItem = WindowsShellItemResolver.TryCreateFromPidl(item.Locator.AbsolutePidl);

		return new PropertyEntry(context.Reference, shellItem is null ? EmptyProperties.Instance : ReadPropertiesCore(shellItem, request, cancellationToken));
	}

	private static IReadOnlyDictionary<string, object?> ReadPropertiesCore(IShellItem shellItem, PropertyRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var details = WindowsShellColumnReader.ReadPropertyDetails(shellItem, request.PropertyIds, request.IncludeFormattedValues, cancellationToken);

		return ReadPropertiesCore(shellItem, request, details, cancellationToken);
	}

	private static IReadOnlyDictionary<string, object?> ReadPropertiesCore(IShellItem? shellItem, PropertyRequest request, WindowsShellPropertyDetails details, CancellationToken cancellationToken)
	{
		var properties = new Dictionary<string, object?>(details.RawValues, StringComparer.Ordinal);
		if (shellItem is IShellItem2 shellItem2)
		{
			foreach (var propertyId in request.PropertyIds)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (properties.ContainsKey(propertyId))
				{
					continue;
				}

				switch (propertyId)
				{
					case ItemTypeText:
						AddString(shellItem2, _itemTypeTextKey, ItemTypeText, properties);
						break;
					case Size:
						AddUInt64(shellItem2, _sizeKey, Size, properties);
						break;
					case DateModified:
						AddFileTime(shellItem2, _dateModifiedKey, DateModified, properties);
						break;
					case DateCreated:
						AddFileTime(shellItem2, _dateCreatedKey, DateCreated, properties);
						break;
					case HomeIsPinned:
						AddBool(shellItem2, _homeIsPinnedKey, HomeIsPinned, properties);
						break;
					default:
						AddProperty(shellItem2, propertyId, properties);
						break;
				}
			}
		}

		if (request.IncludeFormattedValues)
		{
			foreach (var displayValue in details.DisplayValues)
			{
				properties.TryGetValue(displayValue.Key, out var rawValue);
				properties[displayValue.Key] = new FormattedPropertyValue(rawValue, displayValue.Value);
			}
		}

		return new ReadOnlyDictionary<string, object?>(properties);
	}

	private static bool HasAllProperties(WindowsShellPropertyDetails details, PropertyRequest request)
	{
		foreach (var propertyId in request.PropertyIds)
		{
			if (!details.RawValues.ContainsKey(propertyId))
			{
				return false;
			}
		}

		return true;
	}

	private static unsafe void AddString(IShellItem2 item, PROPERTYKEY key, string propertyId, Dictionary<string, object?> properties)
	{
		var result = item.GetString(key, out var nativeValue);

		if (result.Failed)
		{
			return;
		}

		try
		{
			properties[propertyId] = nativeValue.ToString();
		}
		finally
		{
			PInvoke.CoTaskMemFree(nativeValue.Value);
		}
	}

	private static void AddUInt64(IShellItem2 item, PROPERTYKEY key, string propertyId, Dictionary<string, object?> properties)
	{
		var result = item.GetUInt64(key, out var value);

		if (result.Succeeded)
		{
			properties[propertyId] = value;
		}
	}

	private static void AddBool(IShellItem2 item, PROPERTYKEY key, string propertyId, Dictionary<string, object?> properties)
	{
		var result = item.GetBool(key, out var value);

		if (result.Succeeded)
		{
			properties[propertyId] = (bool)value;
		}
	}

	private static void AddFileTime(IShellItem2 item, PROPERTYKEY key, string propertyId, Dictionary<string, object?> properties)
	{
		var result = item.GetFileTime(key, out var value);

		if (result.Failed)
		{
			return;
		}

		if (TryConvertFileTime(value, out var converted))
		{
			properties[propertyId] = converted;
		}
	}

	private static bool AddProperty(IShellItem2 item, string propertyId, Dictionary<string, object?> properties)
	{
		var keyResult = PInvoke.PSGetPropertyKeyFromName(propertyId, out var key);
		if (keyResult.Failed)
		{
			return false;
		}

		PROPVARIANT value = default;
		try
		{
			var result = item.GetProperty(in key, out value);
			if (result.Failed)
			{
				return false;
			}

			properties[propertyId] = ReadPropertyValue(in value);

			return true;
		}
		finally
		{
			PInvoke.PropVariantClear(ref value);
		}
	}

	private static unsafe object? ReadPropertyValue(in PROPVARIANT value)
	{
		return (VarEnum)value.vt switch
		{
			VarEnum.VT_EMPTY or VarEnum.VT_NULL => null,
			VarEnum.VT_I1 => (sbyte)value.cVal,
			VarEnum.VT_UI1 => value.bVal,
			VarEnum.VT_I2 => value.iVal,
			VarEnum.VT_UI2 => value.uiVal,
			VarEnum.VT_I4 or VarEnum.VT_INT => value.lVal,
			VarEnum.VT_UI4 or VarEnum.VT_UINT => value.ulVal,
			VarEnum.VT_I8 => value.hVal,
			VarEnum.VT_UI8 => value.uhVal,
			VarEnum.VT_R4 => value.fltVal,
			VarEnum.VT_R8 => value.dblVal,
			VarEnum.VT_BOOL => (bool)value.boolVal,
			VarEnum.VT_DATE => TryConvertOaDate(value.date, out var dateValue) ? dateValue : null,
			VarEnum.VT_BSTR => value.bstrVal.ToString(),
			VarEnum.VT_LPSTR => value.pszVal.ToString(),
			VarEnum.VT_LPWSTR => value.pwszVal.ToString(),
			VarEnum.VT_FILETIME => TryConvertFileTime(value.filetime, out var converted) ? converted : null,
			VarEnum.VT_CLSID => value.puuid is null ? null : *value.puuid,
			_ => null,
		};
	}

	private static bool TryConvertOaDate(double value, out DateTime converted)
	{
		if (!double.IsFinite(value) || value <= MinimumOaDate || value >= MaximumOaDate)
		{
			converted = default;

			return false;
		}

		converted = DateTime.FromOADate(value);

		return true;
	}

	private static bool TryConvertFileTime(System.Runtime.InteropServices.ComTypes.FILETIME value, out DateTimeOffset converted)
	{
		var fileTime = ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
		if (fileTime > _maximumFileTime)
		{
			converted = default;

			return false;
		}

		converted = DateTimeOffset.FromFileTime((long)fileTime);

		return true;
	}

	private static PROPERTYKEY ResolvePropertyKey(string propertyId)
	{
		var result = PInvoke.PSGetPropertyKeyFromName(propertyId, out var key);
		result.ThrowOnFailure();

		return key;
	}

	private sealed record PropertyEntry(StorableReference Reference, IReadOnlyDictionary<string, object?> Properties);

	private static class EmptyProperties
	{
		public static IReadOnlyDictionary<string, object?> Instance { get; }
			= new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
	}

	private static class EmptyResults
	{
		public static IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>> Instance { get; }
			= new ReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>(new Dictionary<StorableReference, IReadOnlyDictionary<string, object?>>());
	}
}

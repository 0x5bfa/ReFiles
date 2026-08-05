// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Files.Core.Diagnostics;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;
using Files.Core.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Reads Windows Shell properties for filesystem items.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class WindowsPropertyReader : IPropertyReader
{
	private const string ItemTypeText = "System.ItemTypeText";
	private const string Size = "System.Size";
	private const string DateModified = "System.DateModified";
	private const string DateCreated = "System.DateCreated";
	private const string HomeIsPinned = "System.Home.IsPinned";

	private static readonly PROPERTYKEY _itemTypeTextKey = ResolvePropertyKey(ItemTypeText);
	private static readonly PROPERTYKEY _sizeKey = ResolvePropertyKey(Size);
	private static readonly PROPERTYKEY _dateModifiedKey = ResolvePropertyKey(DateModified);
	private static readonly PROPERTYKEY _dateCreatedKey = ResolvePropertyKey(DateCreated);
	private static readonly PROPERTYKEY _homeIsPinnedKey = new()
	{
		fmtid = new Guid(0x30C8EEF4u, 0xA832, 0x41E2, 0xAB, 0x32, 0xE3, 0xC3, 0xCA, 0x28, 0xFD, 0x29),
		pid = 4,
	};

	public bool CanRead(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.Source is WindowsStorageSource
			&& context.CoreModel is WindowsStorable;
	}

	public async ValueTask<IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>> GetPropertiesAsync(
		PropertyRequest request,
		IReadOnlyList<ItemContext> contexts,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(contexts);

		var startTimestamp = Stopwatch.GetTimestamp();
		CoreDiagnosticLog.Write("WindowsPropertyReader", $"GetProperties START propertyCount={request.PropertyIds.Count} contextCount={contexts.Count}");

		var tasks = contexts.Where(CanRead).Select(context => ReadOneAsync(request, context, cancellationToken)).ToArray();

		if (tasks.Length is 0)
		{
			CoreDiagnosticLog.Write("WindowsPropertyReader", $"GetProperties END readableContexts=0 elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

			return EmptyResults.Instance;
		}

		var entries = await Task.WhenAll(tasks).ConfigureAwait(false);

		var results = entries.ToDictionary(static entry => entry.Reference, static entry => entry.Properties);
		CoreDiagnosticLog.Write(
			"WindowsPropertyReader",
			$"GetProperties END readableContexts={tasks.Length} resultCount={results.Count} elapsedMs={Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:F1}");

		return new ReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>(results);
	}

	private static Task<PropertyEntry> ReadOneAsync(PropertyRequest request, ItemContext context, CancellationToken cancellationToken)
	{
		var source = (WindowsStorageSource)context.Source;
		var item = (WindowsStorable)context.CoreModel;

		return source.ShellItemResolver.InvokeConcurrentAsync(
			((WindowsStorable)item).Locator,
			shellItem => new PropertyEntry(context.Reference, ReadPropertiesCore(shellItem, item.ParsingName, request, cancellationToken)),
			cancellationToken);
	}

	private static IReadOnlyDictionary<string, object?> ReadPropertiesCore(IShellItem shellItem, string parsingName, PropertyRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (shellItem is not IShellItem2 shellItem2)
		{
			return EmptyProperties.Instance;
		}

		var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
		var detailsPropertyIds = new List<string>();

		foreach (var propertyId in request.PropertyIds)
		{
			cancellationToken.ThrowIfCancellationRequested();

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
					detailsPropertyIds.Add(propertyId);
					break;
			}
		}

		if (detailsPropertyIds.Count is not 0)
		{
			var detailsProperties = WindowsShellColumnReader.ReadValues(parsingName, detailsPropertyIds, cancellationToken);
			foreach (var property in detailsProperties)
			{
				properties[property.Key] = property.Value;
			}
		}

		return new ReadOnlyDictionary<string, object?>(properties);
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

		var fileTime = ((long)value.dwHighDateTime << 32)
			| (long)value.dwLowDateTime;
		properties[propertyId] = DateTimeOffset.FromFileTime(fileTime);
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

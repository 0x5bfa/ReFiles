// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Files.Core.Windows;

internal static class WindowsFileExplorerAppExtensionCatalog
{
	private const int MaximumCachedItemTypes = 256;
	private const int RegistrationCacheLifetimeSeconds = 60;
	private const uint WaitForStateRepository = 1;
	private const string RuntimeClassName = "Windows.Internal.FileExplorerAppExtension";
	// The Shell cache invalidates internally; expire this process-local snapshot when no public invalidation signal is available.
	private static readonly long RegistrationCacheLifetimeTicks = Stopwatch.Frequency * RegistrationCacheLifetimeSeconds;
	private static readonly Lock _cacheLock = new();
	private static readonly Dictionary<string, ExtensionCacheEntry> _extensionsByItemType = new(StringComparer.OrdinalIgnoreCase);

	internal static IReadOnlyList<WindowsFileExplorerAppExtensionGroup> GetExtensions(IEnumerable<string> itemTypes)
	{
		ArgumentNullException.ThrowIfNull(itemTypes);

		var uniqueItemTypes = itemTypes.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (uniqueItemTypes.Length is 0)
		{
			return [];
		}

		lock (_cacheLock)
		{
			var timestamp = Stopwatch.GetTimestamp();
			RemoveExpiredCacheEntries(timestamp);
			var extensionsByItemType = new Dictionary<string, IReadOnlyList<WindowsFileExplorerAppExtensionGroup>>(StringComparer.OrdinalIgnoreCase);
			var itemTypesToLoad = new List<string>();
			foreach (var itemType in uniqueItemTypes)
			{
				if (_extensionsByItemType.TryGetValue(itemType, out var cached))
				{
					extensionsByItemType.Add(itemType, cached.Extensions);
				}
				else
				{
					itemTypesToLoad.Add(itemType);
				}
			}

			if (itemTypesToLoad.Count is not 0)
			{
				var hr = PInvoke.WindowsCreateString(RuntimeClassName, checked((uint)RuntimeClassName.Length), out var className);
				if (hr.Succeeded)
				{
					using (className)
					{
						hr = PInvoke.RoGetActivationFactory(className, out IFileExplorerAppExtensionStatics factory);
						if (hr.Succeeded && factory is not null)
						{
							foreach (var itemType in itemTypesToLoad)
							{
								var itemExtensions = new List<WindowsFileExplorerAppExtensionGroup>();
								var isComplete = AppendExtensions(factory, itemType, itemExtensions);
								var cachedExtensions = itemExtensions.ToArray();
								extensionsByItemType.Add(itemType, cachedExtensions);
								if (isComplete)
								{
									CacheExtensions(itemType, cachedExtensions, Stopwatch.GetTimestamp());
								}
							}
						}
					}
				}
			}

			var mergedGroups = new List<MutableExtensionGroup>();
			var groupsByDisplayName = new Dictionary<string, MutableExtensionGroup>(StringComparer.OrdinalIgnoreCase);
			var identifiers = new HashSet<(Guid ClassId, string VerbId)>();
			foreach (var itemType in uniqueItemTypes)
			{
				if (extensionsByItemType.TryGetValue(itemType, out var itemExtensions))
				{
					foreach (var extension in itemExtensions)
					{
						MutableExtensionGroup group;
						if (string.IsNullOrWhiteSpace(extension.DisplayName))
						{
							group = new(extension);
							mergedGroups.Add(group);
						}
						else if (!groupsByDisplayName.TryGetValue(extension.DisplayName, out group!))
						{
							group = new(extension);
							groupsByDisplayName.Add(extension.DisplayName, group);
							mergedGroups.Add(group);
						}
						else if (!string.Equals(group.PackageFullName, extension.PackageFullName, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						else
						{
							group.MergeMetadata(extension);
						}

						group.AppendUniqueRegistrations(extension.Registrations, identifiers);
					}
				}
			}

			return mergedGroups.OrderBy(static group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
				.Select(static group => group.ToExtensionGroup()).Where(static group => group.Registrations.Count is not 0).ToArray();
		}
	}

	internal static IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> GetRegistrations(IEnumerable<string> itemTypes)
	{
		return GetExtensions(itemTypes).SelectMany(static extension => extension.Registrations).ToArray();
	}

	internal static void ResetCache()
	{
		lock (_cacheLock)
		{
			_extensionsByItemType.Clear();
		}
	}

	private static bool AppendExtensions(IFileExplorerAppExtensionStatics factory, string itemType, List<WindowsFileExplorerAppExtensionGroup> extensionGroups)
	{
		var hr = factory.GetExtensions(itemType, WaitForStateRepository, out var extensionsObject);
		if (hr.Failed || extensionsObject is null || !ExtensionVectorAdapter.TryCreate(extensionsObject, out var extensionVector))
		{
			return false;
		}

		if (!extensionVector.TryGetSize(out var extensionCount))
		{
			return false;
		}

		var isComplete = true;
		for (uint extensionIndex = 0; extensionIndex < extensionCount; extensionIndex++)
		{
			if (extensionVector.TryGetAt(extensionIndex, out var extension))
			{
				if (!TryCreateExtensionGroup(extension, out var extensionGroup, out var complete))
				{
					isComplete = false;
				}
				else if (extensionGroup is not null)
				{
					extensionGroups.Add(extensionGroup);
					isComplete &= complete;
				}
			}
			else
			{
				isComplete = false;
			}
		}

		return isComplete;
	}

	private static bool TryCreateExtensionGroup(ExtensionAdapter extension, out WindowsFileExplorerAppExtensionGroup? extensionGroup, out bool isComplete)
	{
		isComplete = extension.TryGetDisplayName(out var displayName);
		if (!extension.TryGetPackageFullName(out var packageFullName))
		{
			isComplete = false;
		}

		var isL1Default = extension.GetIsL1Default();

		if (!extension.TryGetVerbs(out var verbs) || verbs is null)
		{
			extensionGroup = null;

			return false;
		}

		extension.TryGetLogoPath(out var logoPath);

		var hr = verbs.GetSize(out var verbCount);
		if (hr.Failed)
		{
			extensionGroup = null;

			return false;
		}

		var registrations = new List<WindowsFileExplorerAppExtensionRegistration>(checked((int)verbCount));
		var identifiers = new HashSet<(Guid ClassId, string VerbId)>();
		for (uint verbIndex = 0; verbIndex < verbCount; verbIndex++)
		{
			hr = verbs.GetAt(verbIndex, out var valueSet);
			if (hr.Failed || valueSet is null)
			{
				isComplete = false;
				continue;
			}

			var verbId = ReadValueSetString(valueSet, "Id") ?? string.Empty;
			var classId = ReadValueSetGuid(valueSet, "Verb");
			if (classId is not { } commandClassId || commandClassId == Guid.Empty || !identifiers.Add((commandClassId, verbId)))
			{
				continue;
			}

			registrations.Add(new(commandClassId, verbId, displayName, packageFullName,
				StaticDisplayName: ReadValueSetString(valueSet, "StaticDisplayName"), StaticIconPath: ReadValueSetString(valueSet, "StaticIconPath")));
		}

		extensionGroup = registrations.Count is 0 ? null : new(packageFullName, displayName, logoPath, isL1Default, registrations);

		return true;
	}

	private static void CacheExtensions(string itemType, WindowsFileExplorerAppExtensionGroup[] extensions, long timestamp)
	{
		if (_extensionsByItemType.Count >= MaximumCachedItemTypes)
		{
			var oldestItemType = _extensionsByItemType.MinBy(static entry => entry.Value.ExpiresAtTimestamp).Key;
			_extensionsByItemType.Remove(oldestItemType);
		}

		_extensionsByItemType[itemType] = new(extensions, timestamp + RegistrationCacheLifetimeTicks);
	}

	private static void RemoveExpiredCacheEntries(long timestamp)
	{
		var expiredItemTypes = _extensionsByItemType.Where(entry => entry.Value.ExpiresAtTimestamp <= timestamp).Select(static entry => entry.Key).ToArray();
		foreach (var itemType in expiredItemTypes)
		{
			_extensionsByItemType.Remove(itemType);
		}
	}

	private sealed class MutableExtensionGroup
	{
		private readonly List<WindowsFileExplorerAppExtensionRegistration> _registrations = [];

		public string PackageFullName { get; }
		public string DisplayName { get; private set; }
		public string? LogoPath { get; private set; }
		public bool? IsL1Default { get; private set; }

		public MutableExtensionGroup(WindowsFileExplorerAppExtensionGroup extension)
		{
			PackageFullName = extension.PackageFullName;
			DisplayName = extension.DisplayName;
			LogoPath = extension.LogoPath;
			IsL1Default = extension.IsL1Default;
		}

		public void MergeMetadata(WindowsFileExplorerAppExtensionGroup extension)
		{
			if (string.IsNullOrWhiteSpace(DisplayName))
			{
				DisplayName = extension.DisplayName;
			}

			if (string.IsNullOrWhiteSpace(LogoPath))
			{
				LogoPath = extension.LogoPath;
			}

			if (IsL1Default is null)
			{
				IsL1Default = extension.IsL1Default;
			}
		}

		public void AppendUniqueRegistrations(IEnumerable<WindowsFileExplorerAppExtensionRegistration> registrations, HashSet<(Guid ClassId, string VerbId)> identifiers)
		{
			foreach (var registration in registrations)
			{
				if (identifiers.Add((registration.ClassId, registration.VerbId)))
				{
					_registrations.Add(registration);
				}
			}
		}

		public WindowsFileExplorerAppExtensionGroup ToExtensionGroup()
		{
			return new(PackageFullName, DisplayName, LogoPath, IsL1Default, _registrations.ToArray());
		}
	}

	private static string? ReadValueSetString(IPropertySet valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value))
		{
			return null;
		}

		var propertyValue = value as IPropertyValue;
		if (propertyValue is null)
		{
			return null;
		}

		var hr = propertyValue.GetString(out var result);

		return hr.Succeeded ? result : null;
	}

	private static Guid? ReadValueSetGuid(IPropertySet valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value))
		{
			return null;
		}

		var propertyValue = value as IPropertyValue;
		if (propertyValue is null)
		{
			return null;
		}

		var hr = propertyValue.GetGuid(out var result);

		return hr.Succeeded ? result : null;
	}

	private static bool TryLookupValue(IPropertySet valueSet, string key, out IInspectable? value)
	{
		value = null;
		var map = valueSet as IStringInspectableMap;
		if (map is null)
		{
			return false;
		}

		var hr = map.Lookup(key, out value);
		if (hr.Failed || value is null)
		{
			value = null;

			return false;
		}

		return true;
	}

	private readonly struct ExtensionVectorAdapter
	{
		private readonly IFileExplorerAppExtensionVectorView? _extensions;
		private readonly IFileExplorerAppExtensionVectorView2? _extensions2;

		private ExtensionVectorAdapter(IFileExplorerAppExtensionVectorView? extensions, IFileExplorerAppExtensionVectorView2? extensions2)
		{
			_extensions = extensions;
			_extensions2 = extensions2;
		}

		public static bool TryCreate(object extensionsObject, out ExtensionVectorAdapter adapter)
		{
			var extensions2 = extensionsObject as IFileExplorerAppExtensionVectorView2;
			if (extensions2 is not null)
			{
				adapter = new(null, extensions2);

				return true;
			}

			var extensions = extensionsObject as IFileExplorerAppExtensionVectorView;
			if (extensions is not null)
			{
				adapter = new(extensions, null);

				return true;
			}

			adapter = default;

			return false;
		}

		public bool TryGetAt(uint index, out ExtensionAdapter extension)
		{
			HRESULT hr;
			if (_extensions2 is not null)
			{
				hr = _extensions2.GetAt(index, out var extension2);
				if (hr.Succeeded && extension2 is not null)
				{
					extension = new(null, extension2);

					return true;
				}
			}

			if (_extensions is not null)
			{
				hr = _extensions.GetAt(index, out var extension1);
				if (hr.Succeeded && extension1 is not null)
				{
					extension = new(extension1, null);

					return true;
				}
			}

			extension = default;

			return false;
		}

		public bool TryGetSize(out uint size)
		{
			if (_extensions2 is not null)
			{
				return _extensions2.GetSize(out size).Succeeded;
			}

			if (_extensions is not null)
			{
				return _extensions.GetSize(out size).Succeeded;
			}

			size = 0;

			return false;
		}
	}

	private readonly struct ExtensionAdapter
	{
		private readonly IFileExplorerAppExtension? _extension;
		private readonly IFileExplorerAppExtension2? _extension2;

		public ExtensionAdapter(IFileExplorerAppExtension? extension, IFileExplorerAppExtension2? extension2)
		{
			_extension = extension;
			_extension2 = extension2;
		}

		public bool TryGetDisplayName(out string displayName)
		{
			if (_extension2 is not null)
			{
				var hr = _extension2.GetDisplayName(out var result);
				displayName = hr.Succeeded ? result ?? string.Empty : string.Empty;

				return hr.Succeeded;
			}

			if (_extension is not null)
			{
				var hr = _extension.GetDisplayName(out var result);
				displayName = hr.Succeeded ? result ?? string.Empty : string.Empty;

				return hr.Succeeded;
			}

			displayName = string.Empty;

			return false;
		}

		public bool TryGetPackageFullName(out string packageFullName)
		{
			if (_extension2 is not null)
			{
				var hr = _extension2.GetPackageFullName(out var result);
				packageFullName = hr.Succeeded ? result ?? string.Empty : string.Empty;

				return hr.Succeeded;
			}

			if (_extension is not null)
			{
				var hr = _extension.GetPackageFullName(out var result);
				packageFullName = hr.Succeeded ? result ?? string.Empty : string.Empty;

				return hr.Succeeded;
			}

			packageFullName = string.Empty;

			return false;
		}

		public bool TryGetLogoPath(out string? logoPath)
		{
			HRESULT hr;
			if (_extension2 is not null)
			{
				hr = _extension2.GetLogo(out logoPath);
			}
			else if (_extension is not null)
			{
				hr = _extension.GetLogo(out logoPath);
			}
			else
			{
				logoPath = null;

				return false;
			}

			if (hr.Failed || string.IsNullOrWhiteSpace(logoPath))
			{
				logoPath = null;

				return false;
			}

			return true;
		}

		public bool? GetIsL1Default()
		{
			if (_extension2 is null)
			{
				return null;
			}

			var hr = _extension2.GetIsL1Default(out var isL1Default);

			return hr.Succeeded ? isL1Default : null;
		}

		public bool TryGetVerbs(out IPropertySetVectorView? verbs)
		{
			HRESULT hr;
			if (_extension2 is not null)
			{
				hr = _extension2.GetVerbs(WaitForStateRepository, out verbs);

				return hr.Succeeded && verbs is not null;
			}

			if (_extension is not null)
			{
				hr = _extension.GetVerbs(WaitForStateRepository, out verbs);

				return hr.Succeeded && verbs is not null;
			}

			verbs = null;

			return false;
		}
	}

	private sealed record ExtensionCacheEntry(WindowsFileExplorerAppExtensionGroup[] Extensions, long ExpiresAtTimestamp);
}

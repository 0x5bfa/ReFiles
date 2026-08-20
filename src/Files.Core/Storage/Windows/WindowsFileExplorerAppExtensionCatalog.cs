// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsFileExplorerAppExtensionCatalog
{
	private const uint WaitForStateRepository = 1;
	private const int DisplayNameSlot = 6;
	private const int PackageFullNameSlot = 8;
	private const int GetVerbsSlot = 9;
	private const int VectorGetAtSlot = 6;
	private const int VectorSizeSlot = 7;
	private const int MapLookupSlot = 6;
	private const int PropertyValueGetStringSlot = 19;
	private const int PropertyValueGetGuidSlot = 20;
	private static readonly Guid _staticsInterfaceId = new("104C1AFF-F09F-5AA1-945F-78737EE0FE45");
	private static readonly Guid _mapViewInterfaceId = new("E480CE40-A338-4ADA-ADCF-272272E48CB9");
	private static readonly Guid _propertyValueInterfaceId = new("4BD682DD-7554-40E9-9A9B-82654BF08D3C");

	internal static IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> GetRegistrations(IEnumerable<string> itemTypes)
	{
		ArgumentNullException.ThrowIfNull(itemTypes);

		var registrations = new List<WindowsFileExplorerAppExtensionRegistration>();
		var identifiers = new HashSet<(Guid ClassId, string VerbId)>();
		if (!TryGetActivationFactory(out var factory))
		{
			return registrations;
		}

		try
		{
			foreach (var itemType in itemTypes.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				AppendRegistrations(factory, itemType, registrations, identifiers);
			}
		}
		finally
		{
			Release(factory);
		}

		return registrations;
	}

	private static void AppendRegistrations(nint factory, string itemType, List<WindowsFileExplorerAppExtensionRegistration> registrations, HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		if (PInvoke.WindowsCreateString(itemType, checked((uint)itemType.Length), out var itemTypeString).Failed)
		{
			return;
		}

		using (itemTypeString)
		{
			nint extensions = 0;
			var getExtensions = (delegate* unmanaged[Stdcall]<nint, nint, uint, nint*, int>)GetVtable(factory)[6];
			var extensionsResult = getExtensions(factory, itemTypeString.DangerousGetHandle(), WaitForStateRepository, &extensions);
			if (extensionsResult < 0)
			{
				Release(extensions);

				return;
			}

			if (extensions is 0)
			{
				return;
			}

			try
			{
				if (!TryGetVectorSize(extensions, out var extensionCount))
				{
					return;
				}

				for (uint extensionIndex = 0; extensionIndex < extensionCount; extensionIndex++)
				{
					if (!TryGetVectorItem(extensions, extensionIndex, out var extension))
					{
						continue;
					}

					try
					{
						AppendExtensionVerbs(extension, registrations, identifiers);
					}
					finally
					{
						Release(extension);
					}
				}
			}
			finally
			{
				Release(extensions);
			}
		}
	}

	private static void AppendExtensionVerbs(nint extension, List<WindowsFileExplorerAppExtensionRegistration> registrations, HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		var displayName = ReadHStringProperty(extension, DisplayNameSlot) ?? string.Empty;
		var packageFullName = ReadHStringProperty(extension, PackageFullNameSlot) ?? string.Empty;
		nint verbs = 0;
		var getVerbs = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)GetVtable(extension)[GetVerbsSlot];
		var verbsResult = getVerbs(extension, WaitForStateRepository, &verbs);
		if (verbsResult < 0)
		{
			Release(verbs);

			return;
		}

		if (verbs is 0)
		{
			return;
		}

		try
		{
			if (!TryGetVectorSize(verbs, out var verbCount))
			{
				return;
			}

			for (uint verbIndex = 0; verbIndex < verbCount; verbIndex++)
			{
				if (!TryGetVectorItem(verbs, verbIndex, out var valueSet))
				{
					continue;
				}

				try
				{
					var verbId = ReadValueSetString(valueSet, "Id") ?? string.Empty;
					var classId = ReadValueSetGuid(valueSet, "Verb");
					if (classId is not { } commandClassId || commandClassId == Guid.Empty || !identifiers.Add((commandClassId, verbId)))
					{
						continue;
					}

					registrations.Add(new(commandClassId, verbId, displayName, packageFullName));
				}
				finally
				{
					Release(valueSet);
				}
			}
		}
		finally
		{
			Release(verbs);
		}
	}

	private static string? ReadValueSetString(nint valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value))
		{
			return null;
		}

		try
		{
			if (!TryQueryInterface(value, _propertyValueInterfaceId, out var propertyValue))
			{
				return null;
			}

			try
			{
				nint result = 0;
				var getString = (delegate* unmanaged[Stdcall]<nint, nint*, int>)GetVtable(propertyValue)[PropertyValueGetStringSlot];
				var stringResult = getString(propertyValue, &result);
				if (stringResult < 0)
				{
					DeleteHString(result);

					return null;
				}

				return ReadAndDeleteHString(result);
			}
			finally
			{
				Release(propertyValue);
			}
		}
		finally
		{
			Release(value);
		}
	}

	private static Guid? ReadValueSetGuid(nint valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value))
		{
			return null;
		}

		try
		{
			if (!TryQueryInterface(value, _propertyValueInterfaceId, out var propertyValue))
			{
				return null;
			}

			try
			{
				Guid result = default;
				var getGuid = (delegate* unmanaged[Stdcall]<nint, Guid*, int>)GetVtable(propertyValue)[PropertyValueGetGuidSlot];

				return getGuid(propertyValue, &result) >= 0 ? result : null;
			}
			finally
			{
				Release(propertyValue);
			}
		}
		finally
		{
			Release(value);
		}
	}

	private static bool TryLookupValue(nint valueSet, string key, out nint value)
	{
		value = 0;
		if (!TryQueryInterface(valueSet, _mapViewInterfaceId, out var map))
		{
			return false;
		}

		try
		{
			if (PInvoke.WindowsCreateString(key, checked((uint)key.Length), out var keyString).Failed)
			{
				return false;
			}

			using (keyString)
			{
				var lookup = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)GetVtable(map)[MapLookupSlot];
				nint lookedUpValue = 0;
				var lookupResult = lookup(map, keyString.DangerousGetHandle(), &lookedUpValue);
				if (lookupResult < 0)
				{
					Release(lookedUpValue);

					return false;
				}

				value = lookedUpValue;

				return value is not 0;
			}
		}
		finally
		{
			Release(map);
		}
	}

	private static string? ReadHStringProperty(nint instance, int slot)
	{
		nint value = 0;
		var getter = (delegate* unmanaged[Stdcall]<nint, nint*, int>)GetVtable(instance)[slot];
		var result = getter(instance, &value);
		if (result < 0)
		{
			DeleteHString(value);

			return null;
		}

		return ReadAndDeleteHString(value);
	}

	private static string? ReadAndDeleteHString(nint value)
	{
		if (value is 0)
		{
			return null;
		}

		try
		{
			uint length;
			var buffer = PInvoke.WindowsGetStringRawBuffer(new HSTRING(value), &length);

			return buffer.Value is null ? string.Empty : new string((char*)buffer.Value, 0, checked((int)length));
		}
		finally
		{
			PInvoke.WindowsDeleteString(new HSTRING(value));
		}
	}

	private static void DeleteHString(nint value)
	{
		if (value is not 0)
		{
			PInvoke.WindowsDeleteString(new HSTRING(value));
		}
	}

	private static bool TryGetActivationFactory(out nint factory)
	{
		factory = 0;
		const string runtimeClassName = "Windows.Internal.FileExplorerAppExtension";
		if (PInvoke.WindowsCreateString(runtimeClassName, checked((uint)runtimeClassName.Length), out var className).Failed)
		{
			return false;
		}

		using (className)
		{
			var interfaceId = _staticsInterfaceId;
			nint activationFactory = 0;
			var activationResult = PInvoke.RoGetActivationFactoryRaw(className.DangerousGetHandle(), &interfaceId, &activationFactory);
			if (activationResult < 0)
			{
				Release(activationFactory);

				return false;
			}

			factory = activationFactory;

			return factory is not 0;
		}
	}

	private static bool TryGetVectorSize(nint vector, out uint size)
	{
		uint vectorSize = 0;
		var getSize = (delegate* unmanaged[Stdcall]<nint, uint*, int>)GetVtable(vector)[VectorSizeSlot];
		var sizeResult = getSize(vector, &vectorSize);
		size = vectorSize;

		return sizeResult >= 0;
	}

	private static bool TryGetVectorItem(nint vector, uint index, out nint item)
	{
		item = 0;
		nint vectorItem = 0;
		var getAt = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)GetVtable(vector)[VectorGetAtSlot];
		var itemResult = getAt(vector, index, &vectorItem);
		if (itemResult < 0)
		{
			Release(vectorItem);

			return false;
		}

		item = vectorItem;

		return item is not 0;
	}

	private static bool TryQueryInterface(nint instance, Guid interfaceId, out nint result)
	{
		result = 0;
		var requestedInterfaceId = interfaceId;
		nint interfacePointer = 0;
		var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)GetVtable(instance)[0];
		var queryResult = queryInterface(instance, &requestedInterfaceId, &interfacePointer);
		if (queryResult < 0)
		{
			Release(interfacePointer);

			return false;
		}

		result = interfacePointer;

		return result is not 0;
	}

	private static void Release(nint instance)
	{
		if (instance is not 0)
		{
			var release = (delegate* unmanaged[Stdcall]<nint, uint>)GetVtable(instance)[2];
			release(instance);
		}
	}

	private static nint* GetVtable(nint instance) => *(nint**)instance;
}

internal sealed record WindowsFileExplorerAppExtensionRegistration(Guid ClassId, string VerbId, string DisplayName, string PackageFullName);

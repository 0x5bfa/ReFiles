// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;

namespace Files.Core.Windows;

internal static class WindowsFileExplorerAppExtensionCatalog
{
	private const uint WaitForStateRepository = 1;
	private const string RuntimeClassName = "Windows.Internal.FileExplorerAppExtension";

	internal static IReadOnlyList<WindowsFileExplorerAppExtensionRegistration> GetRegistrations(IEnumerable<string> itemTypes)
	{
		ArgumentNullException.ThrowIfNull(itemTypes);

		var registrations = new List<WindowsFileExplorerAppExtensionRegistration>();
		var identifiers = new HashSet<(Guid ClassId, string VerbId)>();
		var hr = PInvoke.WindowsCreateString(RuntimeClassName, checked((uint)RuntimeClassName.Length), out var className);
		if (hr.Failed)
		{
			return registrations;
		}

		using (className)
		{
			hr = PInvoke.RoGetActivationFactory(className, out IFileExplorerAppExtensionStatics factory);
			if (hr.Failed || factory is null)
			{
				return registrations;
			}

			foreach (var itemType in itemTypes.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				AppendRegistrations(factory, itemType, registrations, identifiers);
			}
		}

		return registrations;
	}

	private static void AppendRegistrations(IFileExplorerAppExtensionStatics factory, string itemType, List<WindowsFileExplorerAppExtensionRegistration> registrations,
		HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		var hr = factory.GetExtensions(itemType, WaitForStateRepository, out var extensionsObject);
		if (hr.Failed || extensionsObject is null || !ExtensionVectorAdapter.TryCreate(extensionsObject, out var extensions))
		{
			return;
		}

		if (!extensions.TryGetSize(out var extensionCount))
		{
			return;
		}

		for (uint extensionIndex = 0; extensionIndex < extensionCount; extensionIndex++)
		{
			if (extensions.TryGetAt(extensionIndex, out var extension))
			{
				AppendExtensionVerbs(extension, registrations, identifiers);
			}
		}
	}

	private static void AppendExtensionVerbs(ExtensionAdapter extension, List<WindowsFileExplorerAppExtensionRegistration> registrations, HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		var displayName = extension.GetDisplayName();
		var packageFullName = extension.GetPackageFullName();
		if (!extension.TryGetVerbs(out var verbs) || verbs is null)
		{
			return;
		}

		var hr = verbs.GetSize(out var verbCount);
		if (hr.Failed)
		{
			return;
		}

		for (uint verbIndex = 0; verbIndex < verbCount; verbIndex++)
		{
			hr = verbs.GetAt(verbIndex, out var valueSet);
			if (hr.Failed || valueSet is null)
			{
				continue;
			}

			var verbId = ReadValueSetString(valueSet, "Id") ?? string.Empty;
			var classId = ReadValueSetGuid(valueSet, "Verb");
			if (classId is not { } commandClassId || commandClassId == Guid.Empty || !identifiers.Add((commandClassId, verbId)))
			{
				continue;
			}

			registrations.Add(new(commandClassId, verbId, displayName, packageFullName));
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

		public string GetDisplayName()
		{
			if (_extension2 is not null)
			{
				return _extension2.GetDisplayName(out var displayName).Succeeded ? displayName ?? string.Empty : string.Empty;
			}

			if (_extension is not null)
			{
				return _extension.GetDisplayName(out var displayName).Succeeded ? displayName ?? string.Empty : string.Empty;
			}

			return string.Empty;
		}

		public string GetPackageFullName()
		{
			if (_extension2 is not null)
			{
				return _extension2.GetPackageFullName(out var packageFullName).Succeeded ? packageFullName ?? string.Empty : string.Empty;
			}

			if (_extension is not null)
			{
				return _extension.GetPackageFullName(out var packageFullName).Succeeded ? packageFullName ?? string.Empty : string.Empty;
			}

			return string.Empty;
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
}

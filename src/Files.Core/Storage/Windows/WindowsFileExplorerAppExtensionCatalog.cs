// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices.Marshalling;
using Files.Core.Interop.Windows;
using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Files.Core.Storage.Windows;

internal static class WindowsFileExplorerAppExtensionCatalog
{
	private const uint WaitForStateRepository = 1;

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
			ReleaseComObject(factory);
		}

		return registrations;
	}

	private static void AppendRegistrations(IFileExplorerAppExtensionStatics factory, string itemType, List<WindowsFileExplorerAppExtensionRegistration> registrations,
		HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		var extensionsResult = factory.GetExtensions(itemType, WaitForStateRepository, out var extensions);
		if (extensionsResult.Failed || extensions is null)
		{
			ReleaseComObject(extensions);

			return;
		}

		try
		{
			if (!ExtensionVectorAdapter.TryCreate(extensions, out var extensionVector))
			{
				return;
			}

			if (!extensionVector.TryGetSize(out var extensionCount))
			{
				return;
			}

			for (uint extensionIndex = 0; extensionIndex < extensionCount; extensionIndex++)
			{
				if (!extensionVector.TryGetAt(extensionIndex, out var extension))
				{
					continue;
				}

				try
				{
					AppendExtensionVerbs(extension, registrations, identifiers);
				}
				finally
				{
					extension.Release();
				}
			}
		}
		finally
		{
			ReleaseComObject(extensions);
		}
	}

	private static void AppendExtensionVerbs(ExtensionAdapter extension, List<WindowsFileExplorerAppExtensionRegistration> registrations, HashSet<(Guid ClassId, string VerbId)> identifiers)
	{
		var displayName = extension.GetDisplayName();
		var packageFullName = extension.GetPackageFullName();
		if (!extension.TryGetVerbs(out var verbs) || verbs is null)
		{
			ReleaseComObject(verbs);

			return;
		}

		try
		{
			if (verbs.GetSize(out var verbCount).Failed)
			{
				return;
			}

			for (uint verbIndex = 0; verbIndex < verbCount; verbIndex++)
			{
				if (verbs.GetAt(verbIndex, out var valueSet).Failed || valueSet is null)
				{
					ReleaseComObject(valueSet);

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
					ReleaseComObject(valueSet);
				}
			}
		}
		finally
		{
			ReleaseComObject(verbs);
		}
	}

	private static string? ReadValueSetString(IWinRtPropertySet valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value) || value is null)
		{
			return null;
		}

		try
		{
			if (value is not IWinRtPropertyValue propertyValue)
			{
				return null;
			}

			return propertyValue.GetString(out var result).Succeeded ? result : null;
		}
		finally
		{
			ReleaseComObject(value);
		}
	}

	private static Guid? ReadValueSetGuid(IWinRtPropertySet valueSet, string key)
	{
		if (!TryLookupValue(valueSet, key, out var value) || value is null)
		{
			return null;
		}

		try
		{
			if (value is not IWinRtPropertyValue propertyValue)
			{
				return null;
			}

			return propertyValue.GetGuid(out var result).Succeeded ? result : null;
		}
		finally
		{
			ReleaseComObject(value);
		}
	}

	private static bool TryLookupValue(IWinRtPropertySet valueSet, string key, out IWinRtInspectable? value)
	{
		value = null;
		if (valueSet is not IWinRtStringInspectableMap map)
		{
			return false;
		}

		var result = map.Lookup(key, out value);
		if (result.Failed || value is null)
		{
			ReleaseComObject(value);
			value = null;

			return false;
		}

		return true;
	}

	private static bool TryGetActivationFactory(out IFileExplorerAppExtensionStatics? factory)
	{
		factory = null;
		const string runtimeClassName = "Windows.Internal.FileExplorerAppExtension";
		if (PInvoke.WindowsCreateString(runtimeClassName, checked((uint)runtimeClassName.Length), out var className).Failed)
		{
			return false;
		}

		using (className)
		{
			var result = ComActivationNativeMethods.RoGetActivationFactory(className, out factory);
			if (result.Failed || factory is null)
			{
				ReleaseComObject(factory);
				factory = null;

				return false;
			}

			return true;
		}
	}

	private static void ReleaseComObject(object? instance)
	{
		if (instance is ComObject comObject)
		{
			comObject.FinalRelease();
		}
	}

	private readonly struct ExtensionVectorAdapter
	{
		private readonly IFileExplorerAppExtensionVectorView8972? _extensions8972;
		private readonly IFileExplorerAppExtensionVectorView9278? _extensions9278;

		private ExtensionVectorAdapter(IFileExplorerAppExtensionVectorView8972? extensions8972, IFileExplorerAppExtensionVectorView9278? extensions9278)
		{
			_extensions8972 = extensions8972;
			_extensions9278 = extensions9278;
		}

		public static bool TryCreate(IWinRtInspectable extensions, out ExtensionVectorAdapter adapter)
		{
			if (extensions is IFileExplorerAppExtensionVectorView9278 extensions9278)
			{
				adapter = new(null, extensions9278);

				return true;
			}

			if (extensions is IFileExplorerAppExtensionVectorView8972 extensions8972)
			{
				adapter = new(extensions8972, null);

				return true;
			}

			adapter = default;

			return false;
		}

		public bool TryGetAt(uint index, out ExtensionAdapter extension)
		{
			if (!TryGetInspectable(index, out var inspectable) || inspectable is null)
			{
				ReleaseComObject(inspectable);
				extension = default;

				return false;
			}

			if (ExtensionAdapter.TryCreate(inspectable, out extension))
			{
				return true;
			}

			ReleaseComObject(inspectable);

			return false;
		}

		public bool TryGetSize(out uint size)
		{
			if (_extensions9278 is not null)
			{
				return _extensions9278.GetSize(out size).Succeeded;
			}

			if (_extensions8972 is not null)
			{
				return _extensions8972.GetSize(out size).Succeeded;
			}

			size = 0;

			return false;
		}

		private bool TryGetInspectable(uint index, out IWinRtInspectable? extension)
		{
			if (_extensions9278 is not null)
			{
				return _extensions9278.GetAt(index, out extension).Succeeded && extension is not null;
			}

			if (_extensions8972 is not null)
			{
				return _extensions8972.GetAt(index, out extension).Succeeded && extension is not null;
			}

			extension = null;

			return false;
		}
	}

	private readonly struct ExtensionAdapter
	{
		private readonly IFileExplorerAppExtension8972? _extension8972;
		private readonly IFileExplorerAppExtension9278? _extension9278;

		private ExtensionAdapter(IFileExplorerAppExtension8972? extension8972, IFileExplorerAppExtension9278? extension9278)
		{
			_extension8972 = extension8972;
			_extension9278 = extension9278;
		}

		public static bool TryCreate(IWinRtInspectable extension, out ExtensionAdapter adapter)
		{
			if (extension is IFileExplorerAppExtension9278 extension9278)
			{
				adapter = new(null, extension9278);

				return true;
			}

			if (extension is IFileExplorerAppExtension8972 extension8972)
			{
				adapter = new(extension8972, null);

				return true;
			}

			adapter = default;

			return false;
		}

		public string GetDisplayName()
		{
			if (_extension9278 is not null)
			{
				return _extension9278.GetDisplayName(out var displayName).Succeeded ? displayName ?? string.Empty : string.Empty;
			}

			if (_extension8972 is not null)
			{
				return _extension8972.GetDisplayName(out var displayName).Succeeded ? displayName ?? string.Empty : string.Empty;
			}

			return string.Empty;
		}

		public string GetPackageFullName()
		{
			if (_extension9278 is not null)
			{
				return _extension9278.GetPackageFullName(out var packageFullName).Succeeded ? packageFullName ?? string.Empty : string.Empty;
			}

			if (_extension8972 is not null)
			{
				return _extension8972.GetPackageFullName(out var packageFullName).Succeeded ? packageFullName ?? string.Empty : string.Empty;
			}

			return string.Empty;
		}

		public bool TryGetVerbs(out IFileExplorerValueSetVectorView? verbs)
		{
			if (_extension9278 is not null)
			{
				var result = _extension9278.GetVerbs(WaitForStateRepository, out verbs);

				return result.Succeeded && verbs is not null;
			}

			if (_extension8972 is not null)
			{
				var result = _extension8972.GetVerbs(WaitForStateRepository, out verbs);

				return result.Succeeded && verbs is not null;
			}

			verbs = null;

			return false;
		}

		public void Release()
		{
			if (_extension9278 is not null)
			{
				ReleaseComObject(_extension9278);

				return;
			}

			ReleaseComObject(_extension8972);
		}
	}
}

internal sealed record WindowsFileExplorerAppExtensionRegistration(Guid ClassId, string VerbId, string DisplayName, string PackageFullName);

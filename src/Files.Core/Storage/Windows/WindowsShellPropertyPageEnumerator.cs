// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Registry;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsShellPropertyPageEnumerator
{
	private const uint DialogResourceType = 5;
	private const uint MaximumAssociationKeys = 64;
	private const int MaximumProviderNameLength = 256;
	private const int MaximumProviderValueLength = 128;
	private const string PropertySheetHandlersSubKey = "shellex\\PropertySheetHandlers";
	private const uint PropertySheetPageDialogIndirect = 0x00000001;
	private const uint PropertySheetPageUseTitle = 0x00000008;
	private const uint MinimumPropertySheetPageSize = 40;
	private static readonly Guid _shellFileDefaultExtension = new("21B22460-3AEA-1069-A2DC-08002B30309D");

	[ThreadStatic]
	private static PropertyPageCapture? _activeCapture;

	internal static IReadOnlyList<WindowsShellPropertyPage> GetPages(IShellItemArray selection)
	{
		ArgumentNullException.ThrowIfNull(selection);

		if (selection.BindToHandler<IDataObject>(null, PInvoke.BHID_DataObject, out var dataObject).Failed || dataObject is null || selection.GetItemAt(0, out var primaryItem).Failed)
		{
			return [];
		}

		if (PInvoke.SHGetIDListFromObject(primaryItem, out var primaryPidl).Failed || primaryPidl is null)
		{
			return [];
		}

		Span<HKEY> associationKeys = stackalloc HKEY[checked((int)MaximumAssociationKeys)];
		var pages = new List<WindowsShellPropertyPage>();
		var providers = new List<PropertyPageProvider>();
		var providerClassIds = new HashSet<Guid>();
		uint associationKeyCount = 0;
		try
		{
			fixed (HKEY* associationKeyPointer = associationKeys)
			{
				associationKeyCount = Math.Min(PInvoke.SHGetAssocKeysForIDListRaw(primaryPidl, associationKeyPointer, MaximumAssociationKeys), MaximumAssociationKeys);
			}

			var firstAssociationKey = GetFirstAssociationKey(associationKeys, associationKeyCount);
			if (!firstAssociationKey.IsNull)
			{
				providers.Add(new PropertyPageProvider(_shellFileDefaultExtension, firstAssociationKey, true));
				providerClassIds.Add(_shellFileDefaultExtension);
			}

			for (var index = 0; index < associationKeyCount; index++)
			{
				if (!associationKeys[index].IsNull)
				{
					AddRegisteredProviders(associationKeys[index], providers, providerClassIds);
				}
			}

			foreach (var provider in providers)
			{
				AddProviderPages(provider, dataObject, pages);
			}
		}
		finally
		{
			for (var index = 0; index < associationKeyCount; index++)
			{
				if (!associationKeys[index].IsNull)
				{
					PInvoke.RegCloseKey(associationKeys[index]);
				}
			}

			PInvoke.CoTaskMemFree(primaryPidl);
		}

		return pages;
	}

	private static HKEY GetFirstAssociationKey(Span<HKEY> associationKeys, uint count)
	{
		for (var index = 0; index < count; index++)
		{
			if (!associationKeys[index].IsNull)
			{
				return associationKeys[index];
			}
		}

		return default;
	}

	private static void AddRegisteredProviders(HKEY associationKey, List<PropertyPageProvider> providers, HashSet<Guid> providerClassIds)
	{
		HKEY handlersKey = default;
		fixed (char* subKeyPointer = PropertySheetHandlersSubKey)
		{
			var openResult = PInvoke.RegOpenKeyEx(associationKey, new PCWSTR(subKeyPointer), 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_ENUMERATE_SUB_KEYS, &handlersKey);
			if (openResult != WIN32_ERROR.ERROR_SUCCESS)
			{
				return;
			}
		}

		try
		{
			Span<char> providerName = stackalloc char[MaximumProviderNameLength];
			for (uint index = 0; ; index++)
			{
				providerName.Clear();
				WIN32_ERROR enumerateResult;
				fixed (char* providerNamePointer = providerName)
				{
					enumerateResult = PInvoke.RegEnumKey(handlersKey, index, new PWSTR(providerNamePointer), checked((uint)providerName.Length));
				}

				if (enumerateResult == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
				{
					break;
				}

				var providerNameLength = providerName.IndexOf('\0');
				var registeredProviderName = providerName[..(providerNameLength < 0 ? providerName.Length : providerNameLength)];
				if (enumerateResult != WIN32_ERROR.ERROR_SUCCESS || ReadProviderClassId(handlersKey, registeredProviderName) is not { } classId || !providerClassIds.Add(classId))
				{
					continue;
				}

				providers.Add(new PropertyPageProvider(classId, associationKey, false));
			}
		}
		finally
		{
			PInvoke.RegCloseKey(handlersKey);
		}
	}

	private static Guid? ReadProviderClassId(HKEY handlersKey, ReadOnlySpan<char> providerName)
	{
		Span<char> providerValue = stackalloc char[MaximumProviderValueLength];
		providerValue.Clear();
		var providerValueSize = checked((uint)(providerValue.Length * sizeof(char)));
		fixed (char* providerNamePointer = providerName)
		fixed (char* providerValuePointer = providerValue)
		{
			var valueResult = PInvoke.RegGetValue(handlersKey, new PCWSTR(providerNamePointer), default, REG_ROUTINE_FLAGS.RRF_RT_REG_SZ, null, providerValuePointer, &providerValueSize);
			var providerValueLength = providerValue.IndexOf('\0');
			var registeredProviderValue = providerValue[..(providerValueLength < 0 ? providerValue.Length : providerValueLength)];
			if (valueResult == WIN32_ERROR.ERROR_SUCCESS && Guid.TryParse(registeredProviderValue, out var valueClassId))
			{
				return valueClassId;
			}
		}

		if (Guid.TryParse(providerName, out var nameClassId))
		{
			return nameClassId;
		}

		return null;
	}

	private static void AddProviderPages(PropertyPageProvider provider, IDataObject dataObject, List<WindowsShellPropertyPage> pages)
	{
		var createResult = PInvoke.CoCreateInstance(provider.ClassId, null, CLSCTX.CLSCTX_INPROC_SERVER, out IShellPropSheetExt? extension);
		if (createResult.Failed || extension is null || extension is not IShellExtInit initializer || initializer.Initialize(null, dataObject, provider.AssociationKey).Failed)
		{
			return;
		}

		CapturePages(() => extension.AddPages(&CapturePropertyPage, default), provider.IsDefault, pages);
	}

	private static void CapturePages(Action addPages, bool isDefault, List<WindowsShellPropertyPage> pages)
	{
		var capture = new PropertyPageCapture();
		_activeCapture = capture;
		try
		{
			addPages();
		}
		catch (Exception)
		{
		}
		finally
		{
			_activeCapture = null;
		}

		foreach (var page in capture.Pages)
		{
			try
			{
				pages.Add(new WindowsShellPropertyPage(ReadPageTitle(page) ?? string.Empty, isDefault));
			}
			finally
			{
				PInvoke.DestroyPropertySheetPage(page);
			}
		}
	}

	private static string? ReadPageTitle(HPROPSHEETPAGE page)
	{
		if (page.IsNull)
		{
			return null;
		}

		// The accepted page handle stores the copied PROPSHEETPAGEW after two pointer-sized comctl32 bookkeeping fields.
		var definition = (PROPSHEETPAGEW*)((byte*)page.Value + (2 * sizeof(nint)));
		if (definition->dwSize < MinimumPropertySheetPageSize)
		{
			return null;
		}

		if ((definition->dwFlags & PropertySheetPageUseTitle) is not 0)
		{
			return ReadStringOrResource(definition->hInstance, definition->pszTitle.Value);
		}

		void* dialogTemplate;
		if ((definition->dwFlags & PropertySheetPageDialogIndirect) is not 0)
		{
			dialogTemplate = definition->pResource;
		}
		else
		{
			var module = new HMODULE((nint)definition->hInstance.Value);
			var resource = PInvoke.FindResource(module, definition->pszTemplate, new PCWSTR((char*)DialogResourceType));
			if (resource.IsNull)
			{
				return null;
			}

			var resourceData = PInvoke.LoadResource(module, resource);
			dialogTemplate = resourceData.IsNull ? null : PInvoke.LockResource(resourceData);
		}

		return ReadDialogTemplateTitle((ushort*)dialogTemplate);
	}

	private static string? ReadStringOrResource(HINSTANCE module, char* value)
	{
		if (value is null)
		{
			return null;
		}

		if ((nuint)value > ushort.MaxValue)
		{
			return new string(value);
		}

		Span<char> buffer = stackalloc char[512];
		fixed (char* bufferPointer = buffer)
		{
			var length = PInvoke.LoadString(module, checked((uint)(nuint)value), new PWSTR(bufferPointer), buffer.Length);

			return length > 0 ? new string(bufferPointer, 0, length) : null;
		}
	}

	private static string? ReadDialogTemplateTitle(ushort* dialogTemplate)
	{
		if (dialogTemplate is null)
		{
			return null;
		}

		var field = dialogTemplate[0] is 1 && dialogTemplate[1] is ushort.MaxValue ? dialogTemplate + 13 : dialogTemplate + 9;
		field = SkipDialogTemplateField(field);
		field = SkipDialogTemplateField(field);
		if (*field is 0 || *field is ushort.MaxValue)
		{
			return null;
		}

		return new string((char*)field);
	}

	private static ushort* SkipDialogTemplateField(ushort* field)
	{
		if (*field is 0)
		{
			return field + 1;
		}

		if (*field is ushort.MaxValue)
		{
			return field + 2;
		}

		while (*field is not 0)
		{
			field++;
		}

		return field + 1;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static BOOL CapturePropertyPage(HPROPSHEETPAGE page, LPARAM data)
	{
		try
		{
			if (_activeCapture is { } capture && !page.IsNull)
			{
				capture.Pages.Add(page);

				return true;
			}
		}
		catch (Exception)
		{
		}

		return false;
	}

	private sealed class PropertyPageProvider
	{
		internal Guid ClassId { get; }

		internal HKEY AssociationKey { get; }

		internal bool IsDefault { get; }

		internal PropertyPageProvider(Guid classId, HKEY associationKey, bool isDefault)
		{
			ClassId = classId;
			AssociationKey = associationKey;
			IsDefault = isDefault;
		}
	}

	private sealed class PropertyPageCapture
	{
		internal List<HPROPSHEETPAGE> Pages { get; } = [];
	}
}

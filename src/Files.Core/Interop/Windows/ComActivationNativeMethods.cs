// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Files.Core.Interop.Windows;

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("00000000-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IActivatedComObject
{
}

internal static partial class ComActivationNativeMethods
{
	internal static HRESULT CoCreateInstance<T>(Guid classId, CLSCTX context, out T? instance) where T : class
	{
		var interfaceId = typeof(T).GUID;
		var result = CoCreateInstance(in classId, null, context, in interfaceId, out var activatedObject);

		return CompleteActivation(result, activatedObject, out instance);
	}

	internal static HRESULT CoGetObject<T>(string displayName, ref BIND_OPTS3 bindOptions, out T? instance) where T : class
	{
		var interfaceId = typeof(T).GUID;
		var result = CoGetObject(displayName, ref bindOptions, in interfaceId, out var activatedObject);

		return CompleteActivation(result, activatedObject, out instance);
	}

	internal static HRESULT RoGetActivationFactory<T>(SafeHandle activatableClassId, out T? instance) where T : class
	{
		var interfaceId = typeof(T).GUID;
		var result = RoGetActivationFactory(activatableClassId, in interfaceId, out var activatedObject);

		return CompleteActivation(result, activatedObject, out instance);
	}

	private static HRESULT CompleteActivation<T>(HRESULT result, IActivatedComObject? activatedObject, out T? instance) where T : class
	{
		instance = null;
		if (result.Failed || activatedObject is null)
		{
			ReleaseComObject(activatedObject);

			return result.Failed ? result : HRESULT.E_FAIL;
		}

		instance = activatedObject as T;
		if (instance is null)
		{
			ReleaseComObject(activatedObject);

			return HRESULT.E_NOINTERFACE;
		}

		return result;
	}

	private static void ReleaseComObject(object? instance)
	{
		if (instance is ComObject comObject)
		{
			comObject.FinalRelease();
		}
	}

	[LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial HRESULT CoCreateInstance(in Guid classId, [MarshalUsing(typeof(ComInterfaceMarshaller<IActivatedComObject>))] IActivatedComObject? outer, CLSCTX context, in Guid interfaceId,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IActivatedComObject>))] out IActivatedComObject activatedObject);

	[LibraryImport("ole32.dll", EntryPoint = "CoGetObject", StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial HRESULT CoGetObject(string displayName, ref BIND_OPTS3 bindOptions, in Guid interfaceId,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IActivatedComObject>))] out IActivatedComObject activatedObject);

	[LibraryImport("api-ms-win-core-winrt-l1-1-0.dll", EntryPoint = "RoGetActivationFactory")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static partial HRESULT RoGetActivationFactory(SafeHandle activatableClassId, in Guid interfaceId,
		[MarshalUsing(typeof(UniqueComInterfaceMarshaller<IActivatedComObject>))] out IActivatedComObject activatedObject);
}

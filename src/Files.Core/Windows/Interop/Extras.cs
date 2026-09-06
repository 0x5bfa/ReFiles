// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.Urlmon;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.IO;
using Windows.Win32.System.Search.Common;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32
{
	public static partial class PInvoke
	{
		/// <summary>Specifies the 32-bit ARGB pixel format used by Windows imaging APIs.</summary>
		public const int PixelFormat32bppARGB = 2498570;

		[LibraryImport("ole32.dll", EntryPoint = "CoGetObject", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CoGetObject(string displayName, in BIND_OPTS3 bindOptions, in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? instance);

		/// <summary>Evaluates URL security policy through the Shell URL zone helper exported by ordinal 233.</summary>
		[LibraryImport("shlwapi.dll", EntryPoint = "#233", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT ZoneCheckUrlExCache(
			string url, out uint policy, uint policySize, nint context, uint contextSize, uint action, uint flags, IInternetSecurityMgrSite? securitySite, nint securityManagerCache);

		/// <summary>Reads a file's URL zone from its alternate data stream.</summary>
		[LibraryImport("urlmon.dll", EntryPoint = "GetZoneFromAlternateDataStreamEx", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetZoneFromAlternateDataStreamEx(string filePath, out uint zone);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "GetCachedIniForFolder")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetCachedIniForFolder(uint reserved, in ITEMIDLIST pidl, uint flags, out ICachedPrivateProfile? cachedProfile);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "IsPathOwnedByCurrentUser", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial int IsPathOwnedByCurrentUser(string path);

		[LibraryImport("shlwapi.dll", EntryPoint = "#626", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT SHCreatePropertyBagOnCachedProfileSection(ICachedPrivateProfile cachedProfile, string section, uint mode, in Guid interfaceId, out IPropertyBag? propertyBag);

		[LibraryImport("shlwapi.dll", EntryPoint = "SHGetViewStatePropertyBag", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static unsafe partial HRESULT SHGetViewStatePropertyBag(ITEMIDLIST* pidl, string bagName, uint flags, in Guid interfaceId, out IPropertyBag? propertyBag);

		[LibraryImport("propsys.dll", EntryPoint = "PSGetPropertyDescriptionListFromString", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT PSGetPropertyDescriptionListFromString(string propertyList, in Guid interfaceId, out IPropertyDescriptionList? descriptions);

		[LibraryImport("Windows.Storage.dll", EntryPoint = "CItemStore_CreateInstance")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CItemStoreCreateInstance([MarshalAs(UnmanagedType.Interface)] object? outer, in Guid interfaceId, out IDefViewItemStore? itemStore);

		[LibraryImport("ntshrui.dll", EntryPoint = "CanShareFolder", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT CanShareFolder(string path);

		[LibraryImport("ntshrui.dll", EntryPoint = "ShowShareFolderUI", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT ShowShareFolderUI(HWND owner, string path);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageInstanceCount")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetStorageInstanceCount(uint category, out uint count);

		[LibraryImport("ext-ms-win-storage-sense-l1-1-0.dll", EntryPoint = "GetStorageDeviceInfo")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial HRESULT GetStorageDeviceInfo(uint category, uint index, Span<byte> information);

		/// <summary>Refreshes the Recycle Bin icon.</summary>
		[LibraryImport("shell32.dll", EntryPoint = "SHUpdateRecycleBinIcon", SetLastError = true)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		public static partial void SHUpdateRecycleBinIcon();

		/// <summary>Opens the system device-properties UI for a device instance.</summary>
		/// <param name="parent">The owner window.</param>
		/// <param name="machineName">The optional remote machine name.</param>
		/// <param name="deviceInstanceId">The device instance identifier.</param>
		/// <param name="flags">Reserved flags.</param>
		/// <param name="showDeviceTree">Whether to show the Device Manager tree.</param>
		/// <returns>The native result code.</returns>
		[LibraryImport("devmgr.dll", EntryPoint = "DevicePropertiesExW", StringMarshalling = StringMarshalling.Utf16)]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		public static partial int DevicePropertiesEx(HWND parent, string? machineName, string deviceInstanceId, uint flags, BOOL showDeviceTree);

		[LibraryImport("ntdll.dll", EntryPoint = "NtFsControlFile")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
		internal static partial NTSTATUS NtFsControlFile(SafeFileHandle fileHandle, SafeFileHandle eventHandle, nint apcRoutine, nint apcContext, ref IO_STATUS_BLOCK ioStatusBlock, uint controlCode,
			nint inputBuffer, uint inputBufferLength, ref byte outputBuffer, uint outputBufferLength);
	}
}

namespace Windows.Win32.System.Com
{
	/// <summary>Provides a strongly typed query-continuation service to Windows Shell enumerators.</summary>
	[GeneratedComInterface(Options = ComInterfaceOptions.ManagedObjectWrapper)]
	[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IQueryContinueServiceProvider
	{
		/// <summary>Returns the supported query-continuation service.</summary>
		/// <param name="serviceId">The requested service identifier.</param>
		/// <param name="interfaceId">The requested interface identifier.</param>
		/// <param name="service">Receives the query-continuation service.</param>
		/// <returns>The HRESULT describing whether the service is available.</returns>
		[PreserveSig]
		HRESULT QueryService(in Guid serviceId, in Guid interfaceId, out IQueryContinue? service);
	}
}

namespace Windows.Win32.System.Search
{
	/// <summary>Creates configured Windows Structured Query parsers.</summary>
	[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
	[Guid("A879E3C4-AF77-44FB-8F37-EBD1487CF920")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IQueryParserManager
	{
		/// <summary>Creates and loads a parser for a catalog and keyword language.</summary>
		/// <param name="catalog">The catalog name.</param>
		/// <param name="keywordLanguage">The keyword language identifier.</param>
		/// <param name="interfaceId">The requested parser interface identifier.</param>
		/// <param name="queryParser">Receives the parser.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT CreateLoadedParser(string catalog, ushort keywordLanguage, in Guid interfaceId, out IQueryParser? queryParser);

		/// <summary>Initializes natural-query and wildcard options on a parser.</summary>
		/// <param name="understandNaturalQuerySyntax">Whether natural query syntax is enabled.</param>
		/// <param name="automaticWildcard">Whether automatic wildcard matching is enabled.</param>
		/// <param name="queryParser">The parser to initialize.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT InitializeOptions(BOOL understandNaturalQuerySyntax, BOOL automaticWildcard, IQueryParser? queryParser);

		/// <summary>Sets a parser-manager option.</summary>
		/// <param name="option">The option to set.</param>
		/// <param name="value">The option value.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT SetOption(QUERY_PARSER_MANAGER_OPTION option, in PROPVARIANT value);
	}

	/// <summary>Parses Windows Structured Query input.</summary>
	[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
	[Guid("2EBDEE67-3505-43F8-9946-EA44ABC8E5B0")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal partial interface IQueryParser
	{
		/// <summary>Parses query text into a query solution.</summary>
		/// <param name="input">The query text.</param>
		/// <param name="customProperties">The optional custom-property enumerator.</param>
		/// <param name="solution">Receives the query solution.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT Parse(string input, IEnumUnknown? customProperties, out IQuerySolution? solution);

		/// <summary>Sets a single parser option.</summary>
		/// <param name="option">The option to set.</param>
		/// <param name="value">The option value.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT SetOption(STRUCTURED_QUERY_SINGLE_OPTION option, in PROPVARIANT value);

		/// <summary>Gets a single parser option.</summary>
		/// <param name="option">The option to get.</param>
		/// <param name="value">Receives the option value.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT GetOption(STRUCTURED_QUERY_SINGLE_OPTION option, out PROPVARIANT value);

		/// <summary>Sets a keyed parser option.</summary>
		/// <param name="option">The multi-option to set.</param>
		/// <param name="optionKey">The option key.</param>
		/// <param name="value">The option value.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT SetMultiOption(STRUCTURED_QUERY_MULTIOPTION option, string optionKey, in PROPVARIANT value);

		/// <summary>Gets the parser schema provider.</summary>
		/// <param name="schemaProvider">Receives the schema provider.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT GetSchemaProvider(out ISchemaProvider? schemaProvider);

		/// <summary>Restates a condition as query text.</summary>
		/// <param name="condition">The optional condition.</param>
		/// <param name="useEnglish">Whether to use English keywords.</param>
		/// <param name="queryString">Receives the allocated query text.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT RestateToString(ICondition? condition, BOOL useEnglish, out PWSTR queryString);

		/// <summary>Parses a value for a named property.</summary>
		/// <param name="propertyName">The canonical property name.</param>
		/// <param name="input">The property-value text.</param>
		/// <param name="solution">Receives the query solution.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT ParsePropertyValue(string propertyName, string input, out IQuerySolution? solution);

		/// <summary>Restates a property condition as property and query text.</summary>
		/// <param name="condition">The optional condition.</param>
		/// <param name="useEnglish">Whether to use English keywords.</param>
		/// <param name="propertyName">Receives the allocated property name.</param>
		/// <param name="queryString">Receives the allocated query text.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT RestatePropertyValueToString(ICondition? condition, BOOL useEnglish, out PWSTR propertyName, out PWSTR queryString);
	}

	/// <summary>Contains a parsed Structured Query condition and diagnostics.</summary>
	[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
	[Guid("D6EBC66B-8921-4193-AFDD-A1789FB7FF57")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal unsafe partial interface IQuerySolution : IConditionFactory
	{
		/// <summary>Gets the parsed query condition and optional main entity type.</summary>
		/// <param name="queryNode">Receives the query condition.</param>
		/// <param name="mainType">Receives the optional main entity type.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT GetQuery(out ICondition? queryNode, out IEntity? mainType);

		/// <summary>Gets parse errors through a requested enumerator interface.</summary>
		/// <param name="interfaceId">The requested parse-error interface identifier.</param>
		/// <param name="parseErrors">Receives the parse-error interface.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT GetErrors(in Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? parseErrors);

		/// <summary>Gets the lexical data retained by the parser.</summary>
		/// <param name="inputString">Receives the allocated input text.</param>
		/// <param name="tokens">Receives the token collection.</param>
		/// <param name="locale">Receives the input locale identifier.</param>
		/// <param name="wordBreaker">Receives the word breaker.</param>
		/// <returns>The HRESULT returned by Structured Query.</returns>
		[PreserveSig]
		HRESULT GetLexicalData(out PWSTR inputString, out ITokenCollection? tokens, out uint locale, [MarshalAs(UnmanagedType.Interface)] out object? wordBreaker);
	}
}

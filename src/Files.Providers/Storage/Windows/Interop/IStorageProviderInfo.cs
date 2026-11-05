// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;

namespace Windows.Win32.UI.Shell;

/// <summary>Provides access to a registered Cloud Files sync-root manager.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("692D40A4-EFA1-4089-88F8-15FD6F5F8B64")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface ISyncRootManager
{
	/// <summary>Gets provider information for a full provider and account identifier.</summary>
	/// <param name="providerIdentifier">The full provider and account identifier.</param>
	/// <param name="providerInfo">Receives the matching provider information.</param>
	/// <returns>The HRESULT returned by the sync-root manager.</returns>
	[PreserveSig]
	HRESULT GetStorageProviderInfo(string providerIdentifier, out IStorageProviderInfo? providerInfo);

	/// <summary>Finds the provider information associated with a file-system path.</summary>
	/// <param name="path">The file-system path to resolve.</param>
	/// <param name="providerInfo">Receives the matching provider information.</param>
	/// <param name="resolvedPath">Optionally receives the resolved provider path.</param>
	/// <param name="comparisonFlags">Optionally receives the path-comparison flags.</param>
	/// <returns>The HRESULT returned by the sync-root manager.</returns>
	[PreserveSig]
	HRESULT GetStorageProviderInfoFromPath(string path, out IStorageProviderInfo? providerInfo, PWSTR* resolvedPath, uint* comparisonFlags);
}

/// <summary>Provides the Shell's registered metadata and handlers for a Cloud Files sync root.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("CA01C124-2769-4576-BF12-8A54EE671A86")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IStorageProviderInfo
{
	/// <summary>Gets the full provider, user, and account identifier.</summary>
	/// <param name="identifier">Receives the allocated identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetFullProviderAndUserAndAccountIdentifier(out PWSTR identifier);

	/// <summary>Sets the full provider, user, and account identifier.</summary>
	/// <param name="identifier">The identifier to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetFullProviderAndUserAndAccountIdentifier(string identifier);

	/// <summary>Gets the provider identifier.</summary>
	/// <param name="identifier">Receives the allocated identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetProviderIdentifier(out PWSTR identifier);

	/// <summary>Compares this provider identity with another provider information object.</summary>
	/// <param name="other">The provider information object to compare.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT IsSameIdentifier(IStorageProviderInfo other);

	/// <summary>Gets the provider display name.</summary>
	/// <param name="displayName">Receives the allocated display name.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetDisplayName(out PWSTR displayName);

	/// <summary>Gets the provider display-name resource identifier.</summary>
	/// <param name="resource">Receives the allocated resource identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetDisplayNameResource(out PWSTR resource);

	/// <summary>Sets the provider display-name resource identifier.</summary>
	/// <param name="resource">The resource identifier to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetDisplayNameResource(string resource);

	/// <summary>Gets the provider icon path and resource index.</summary>
	/// <param name="iconPath">Receives the allocated icon path.</param>
	/// <param name="iconIndex">Receives the icon resource index.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetIcon(out PWSTR iconPath, out int iconIndex);

	/// <summary>Gets the provider icon-resource identifier.</summary>
	/// <param name="resource">Receives the allocated resource identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetIconResource(out PWSTR resource);

	/// <summary>Sets the provider icon-resource identifier.</summary>
	/// <param name="resource">The resource identifier to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetIconResource(string resource);

	/// <summary>Sets the provider handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetHandlerClsid(in Guid handlerClassId);

	/// <summary>Gets a provider handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Sets the provider banner-handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetBannerHandlerClsid(in Guid handlerClassId);

	/// <summary>Gets the provider banner handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetBannerHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Sets the provider custom-state-handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetCustomStateHandlerClsid(in Guid handlerClassId);

	/// <summary>Gets the provider custom-state handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetCustomStateHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Sets the provider thumbnail-handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetThumbnailProviderClsid(in Guid handlerClassId);

	/// <summary>Gets the provider thumbnail handler for a path and class identifier.</summary>
	/// <param name="path">The file-system path.</param>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetThumbnailProvider(string path, in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Gets the provider information flags.</summary>
	/// <param name="flags">Receives the provider flags.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetFlags(out uint flags);

	/// <summary>Sets selected provider information flags.</summary>
	/// <param name="flags">The flag values to apply.</param>
	/// <param name="mask">The flags to update.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetFlags(uint flags, uint mask);

	/// <summary>Gets the allocated list of provider custom-state identifiers.</summary>
	/// <param name="stateIds">Receives the allocated identifier array.</param>
	/// <param name="count">Receives the number of identifiers.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetCustomStateIdList(out uint* stateIds, out uint count);

	/// <summary>Gets the display name for a provider custom state.</summary>
	/// <param name="stateId">The custom-state identifier.</param>
	/// <param name="displayName">Receives the allocated display name.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetCustomStateDisplayName(uint stateId, out PWSTR displayName);

	/// <summary>Gets the provider protection mode.</summary>
	/// <param name="protectionMode">Receives the allocated protection mode.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetProtectionMode(out PWSTR protectionMode);

	/// <summary>Sets the provider protection mode.</summary>
	/// <param name="protectionMode">The protection mode to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetProtectionMode(string protectionMode);

	/// <summary>Gets the provider recycle-bin URL.</summary>
	/// <param name="url">Receives the allocated URL.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetRecycleBinUrl(out PWSTR url);

	/// <summary>Sets the provider recycle-bin URL.</summary>
	/// <param name="url">The URL to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetRecycleBinUrl(string url);

	/// <summary>Sets the provider extended-properties-handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetExtendedPropertiesHandlerClsid(in Guid handlerClassId);

	/// <summary>Gets the provider extended-properties handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetExtendedPropertiesHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Gets the provider's registered Cloud Files context-menu class identifiers.</summary>
	/// <param name="count">Receives the number of class identifiers.</param>
	/// <param name="classIds">Receives the allocated class-identifier array.</param>
	/// <param name="reserved">Receives the reserved result value.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetContextMenuVerbs(out uint count, out Guid* classIds, out int reserved);

	/// <summary>Sets the provider's Cloud Files context-menu class identifiers.</summary>
	/// <param name="count">The number of class identifiers.</param>
	/// <param name="classIds">The class-identifier array.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetContextMenuVerbs(uint count, Guid* classIds);

	/// <summary>Sets the provider URI-handler class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetUriHandlerClsid(in Guid handlerClassId);

	/// <summary>Gets the provider URI handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetUriHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Sets the provider application user-model identifier.</summary>
	/// <param name="applicationUserModelId">The application user-model identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetProviderAUMID(string applicationUserModelId);

	/// <summary>Gets the provider application user-model identifier.</summary>
	/// <param name="applicationUserModelId">Receives the allocated identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetProviderAUMID(out PWSTR applicationUserModelId);

	/// <summary>Gets the provider namespace class identifier.</summary>
	/// <param name="namespaceClassId">Receives the class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetNamespaceClsid(out Guid namespaceClassId);

	/// <summary>Sets the provider namespace class identifier.</summary>
	/// <param name="namespaceClassId">The class identifier to set.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetNamespaceClsid(in Guid namespaceClassId);

	/// <summary>Gets the provider icon-overlay handler class identifiers.</summary>
	/// <param name="count">Receives the number of class identifiers.</param>
	/// <param name="classIds">Receives the allocated class-identifier array.</param>
	/// <param name="reserved">Receives the reserved result value.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetIconOverlayHandlerClsidArray(out uint count, out Guid* classIds, out int reserved);

	/// <summary>Sets the provider icon-overlay handler class identifiers.</summary>
	/// <param name="count">The number of class identifiers.</param>
	/// <param name="classIds">The class-identifier array.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetIconOverlayHandlerClsidArray(uint count, Guid* classIds);

	/// <summary>Gets the handler type associated with a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The native handler-type value.</returns>
	[PreserveSig]
	uint GetHandlerType(in Guid handlerClassId);

	/// <summary>Checks whether the provider has a handler of a specified type.</summary>
	/// <param name="handlerType">The native handler-type value.</param>
	/// <param name="hasHandler">Receives a nonzero value when the handler exists.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT HasHandlerType(uint handlerType, out int hasHandler);

	/// <summary>Sets the provider handler-factory class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetHandlerFactoryClsid(in Guid handlerClassId);

	/// <summary>Gets the provider JSON request handler for a class identifier.</summary>
	/// <param name="handlerClassId">The handler class identifier.</param>
	/// <param name="handler">Receives the handler interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetJsonRequestHandler(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? handler);

	/// <summary>Gets the provider event source for an event name and class identifier.</summary>
	/// <param name="eventName">The event name.</param>
	/// <param name="handlerClassId">The event-source class identifier.</param>
	/// <param name="eventSource">Receives the event-source interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetEventSource(string eventName, in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? eventSource);

	/// <summary>Sets the provider copy-hook class identifier.</summary>
	/// <param name="handlerClassId">The copy-hook class identifier.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT SetCopyHookClsid(in Guid handlerClassId);

	/// <summary>Gets the provider copy hook for a class identifier.</summary>
	/// <param name="handlerClassId">The copy-hook class identifier.</param>
	/// <param name="copyHook">Receives the copy-hook interface.</param>
	/// <returns>The HRESULT returned by the provider information object.</returns>
	[PreserveSig]
	HRESULT GetCopyHook(in Guid handlerClassId, [MarshalAs(UnmanagedType.Interface)] out object? copyHook);
}

/// <summary>Provides package metadata associated with a Cloud Files provider.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("CF745728-CBBE-415E-B530-066B3ED219B6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IStorageProviderAppInfo
{
	/// <summary>Sets the provider package full name.</summary>
	/// <param name="packageFullName">The package full name.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT SetPackageFullName(string packageFullName);

	/// <summary>Gets the provider package full name.</summary>
	/// <param name="packageFullName">Receives the allocated package full name.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT GetPackageFullName(out PWSTR packageFullName);

	/// <summary>Sets the provider logo path.</summary>
	/// <param name="logoPath">The logo path.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT SetLogoPath(string logoPath);

	/// <summary>Gets the provider logo path.</summary>
	/// <param name="logoPath">Receives the allocated logo path.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT GetLogoPath(out PWSTR logoPath);

	/// <summary>Sets the provider application display name.</summary>
	/// <param name="displayName">The application display name.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT SetAppDisplayName(string displayName);

	/// <summary>Gets the provider application display name.</summary>
	/// <param name="displayName">Receives the allocated display name.</param>
	/// <returns>The HRESULT returned by the provider application information object.</returns>
	[PreserveSig]
	HRESULT GetAppDisplayName(out PWSTR displayName);
}

/// <summary>Identifies the Shell sync-root manager COM class.</summary>
[Guid("F324E4F9-8496-40B2-A1FF-9617C1C9AFFE")]
internal sealed class SyncRootManagerCoclass
{
}

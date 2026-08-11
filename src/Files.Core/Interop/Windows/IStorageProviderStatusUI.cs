// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Storage.Provider;
using Windows.Win32.Foundation;

namespace Windows.Win32.System.WinRT;

/// <summary>Provides status and command information for a storage provider.</summary>
[GeneratedComInterface, Guid("D6B6A758-198D-5B80-977F-5FF73DA33118"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IStorageProviderStatusUI
{
	/// <summary>Gets the interfaces implemented by the object.</summary>
	/// <param name="iidCount">Receives the number of interface identifiers.</param>
	/// <param name="iids">Receives the interface identifier array.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetIids(out uint iidCount, out /* IID** */ nint iids);

	/// <summary>Gets the WinRT runtime class name.</summary>
	/// <param name="className">Receives the runtime class name.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetRuntimeClassName([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? className);

	/// <summary>Gets the WinRT trust level.</summary>
	/// <param name="trustLevel">Receives the trust level.</param>
	/// <returns>The HRESULT returned by the object.</returns>
	[PreserveSig]
	HRESULT GetTrustLevel(out TrustLevel trustLevel);

	/// <summary>Gets the provider synchronization state.</summary>
	/// <param name="value">Receives the state.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetProviderState(out StorageProviderState value);

	/// <summary>Sets the provider synchronization state.</summary>
	/// <param name="value">The state to expose.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutProviderState(StorageProviderState value);

	/// <summary>Gets the localized provider state label.</summary>
	/// <param name="value">Receives the label.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetProviderStateLabel([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] out string? value);

	/// <summary>Sets the localized provider state label.</summary>
	/// <param name="value">The label to expose.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutProviderStateLabel([MarshalUsing(typeof(global::Windows.Win32.HStringStringMarshaller))] string? value);

	/// <summary>Gets the provider state icon.</summary>
	/// <param name="value">Receives the icon object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetProviderStateIcon(out /* Windows.Foundation.Uri** */ nint value);

	/// <summary>Sets the provider state icon.</summary>
	/// <param name="value">The icon object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutProviderStateIcon(/* Windows.Foundation.Uri* */ nint value);

	/// <summary>Gets the command shown for synchronization status.</summary>
	/// <param name="value">Receives the command object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetSyncStatusCommand(out /* IStorageProviderUICommand** */ nint value);

	/// <summary>Sets the command shown for synchronization status.</summary>
	/// <param name="value">The command object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutSyncStatusCommand(/* IStorageProviderUICommand* */ nint value);

	/// <summary>Gets the quota user interface.</summary>
	/// <param name="value">Receives the quota interface.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetQuotaUI([MarshalAs(UnmanagedType.Interface)] out IStorageProviderQuotaUI value);

	/// <summary>Sets the quota user interface.</summary>
	/// <param name="value">The quota interface.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutQuotaUI([MarshalAs(UnmanagedType.Interface)] IStorageProviderQuotaUI value);

	/// <summary>Gets the additional information user interface.</summary>
	/// <param name="value">Receives the interface pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetMoreInfoUI(out /* IStorageProviderMoreInfoUI** */ nint value);

	/// <summary>Sets the additional information user interface.</summary>
	/// <param name="value">The interface pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutMoreInfoUI(/* IStorageProviderMoreInfoUI* */ nint value);

	/// <summary>Gets the primary provider command.</summary>
	/// <param name="value">Receives the command object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetProviderPrimaryCommand(out /* IStorageProviderUICommand** */ nint value);

	/// <summary>Sets the primary provider command.</summary>
	/// <param name="value">The command object pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutProviderPrimaryCommand(/* IStorageProviderUICommand* */ nint value);

	/// <summary>Gets the secondary provider commands collection.</summary>
	/// <param name="value">Receives the collection pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT GetProviderSecondaryCommands(out /* IVector<IStorageProviderUICommand*>** */ nint value);

	/// <summary>Sets the secondary provider commands collection.</summary>
	/// <param name="value">The collection pointer.</param>
	/// <returns>The HRESULT returned by the provider.</returns>
	[PreserveSig]
	HRESULT PutProviderSecondaryCommands(/* IVector<IStorageProviderUICommand*>* */ nint value);
}

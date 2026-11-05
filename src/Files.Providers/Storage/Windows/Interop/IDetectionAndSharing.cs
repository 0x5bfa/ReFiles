// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFirewall;

namespace Windows.Win32.UI.Shell;

/// <summary>Controls Windows network discovery and file-sharing settings.</summary>
[GeneratedComInterface, Guid("1FDA955C-61FF-11DA-978C-0008744FAAB7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IDetectionAndSharing
{
	/// <summary>Gets the status and recommended action for a sharing feature.</summary>
	/// <param name="type">The feature to inspect.</param>
	/// <param name="state">Receives the current state.</param>
	/// <param name="action">Receives the recommended action.</param>
	/// <returns>The HRESULT returned by the Shell.</returns>
	[PreserveSig]
	HRESULT GetStatus(DTSH_TYPE type, out DTSH_STATE state, out DTSH_ACTION action);

	/// <summary>Turns on a sharing feature.</summary>
	/// <param name="hwnd">The owner window handle.</param>
	/// <param name="type">The feature to enable.</param>
	/// <param name="value">The feature-specific value.</param>
	/// <returns>The HRESULT returned by the Shell.</returns>
	[PreserveSig]
	HRESULT TurnOn(HWND hwnd, DTSH_TYPE type, int value);

	/// <summary>Gets the current Windows Firewall profile.</summary>
	/// <param name="profile">Receives the profile value.</param>
	/// <returns>The HRESULT returned by the Shell.</returns>
	[PreserveSig]
	HRESULT GetCurrentFwProfile(out NET_FW_PROFILE_TYPE2 profile);

	/// <summary>Gets a sharing feature status for a specific firewall profile.</summary>
	/// <param name="profile">The firewall profile.</param>
	/// <param name="type">The feature to inspect.</param>
	/// <param name="state">Receives the current state.</param>
	/// <param name="action">Receives the recommended action.</param>
	/// <returns>The HRESULT returned by the Shell.</returns>
	[PreserveSig]
	HRESULT GetStatusForProfile(NET_FW_PROFILE_TYPE2 profile, DTSH_TYPE type, out DTSH_STATE state, out DTSH_ACTION action);

	/// <summary>Turns on a sharing feature for a specific firewall profile.</summary>
	/// <param name="hwnd">The owner window handle.</param>
	/// <param name="profile">The firewall profile.</param>
	/// <param name="type">The feature to enable.</param>
	/// <param name="value">The feature-specific value.</param>
	/// <returns>The HRESULT returned by the Shell.</returns>
	[PreserveSig]
	HRESULT TurnOnForProfile(HWND hwnd, NET_FW_PROFILE_TYPE2 profile, DTSH_TYPE type, int value);
}

/// <summary>Identifies a network discovery or file-sharing feature.</summary>
public enum DTSH_TYPE
{
	/// <summary>Network discovery.</summary>
	DTSH_NETWORK_DISCOVERY = 0,
	/// <summary>File sharing.</summary>
	DTSH_FILE_SHARING = 1,
}

/// <summary>Describes whether a sharing feature is enabled.</summary>
public enum DTSH_STATE
{
	/// <summary>The feature is disabled.</summary>
	DTSH_OFF = 0,
	/// <summary>The feature is enabled.</summary>
	DTSH_ON = 1,
}

/// <summary>Identifies an action suggested by the sharing service.</summary>
public enum DTSH_ACTION
{
	/// <summary>No action is required.</summary>
	DTSH_NONE = 0,
}

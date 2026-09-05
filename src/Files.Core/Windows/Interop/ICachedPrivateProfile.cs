// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace Windows.Win32.UI.Shell;

/// <summary>Reads and writes the Shell's cached private-profile representation.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("B57046BC-32E5-428A-9887-19F712B907BF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ICachedPrivateProfile
{
	/// <summary>Initializes the profile from a file path.</summary>
	/// <param name="path">The profile path.</param>
	/// <param name="flags">The initialization flags.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT Initialize(string path, uint flags);

	/// <summary>Initializes the profile from a stream.</summary>
	/// <param name="stream">The source stream.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT InitializeFromStream(IStream stream);

	/// <summary>Reads a value as a PROPVARIANT.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="key">The value name.</param>
	/// <param name="flags">The read flags.</param>
	/// <param name="valueType">The requested variant type.</param>
	/// <param name="value">Receives the value.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT GetPropVariant(string section, string key, uint flags, VARENUM valueType, out PROPVARIANT value);

	/// <summary>Reads a string value.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="key">The value name.</param>
	/// <param name="defaultValue">The fallback value.</param>
	/// <param name="flags">The read flags.</param>
	/// <param name="value">Receives the allocated string.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT GetString(string? section, string? key, string? defaultValue, uint flags, out PWSTR value);

	/// <summary>Reads all values in a section.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="values">Receives the allocated multistring.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT GetSection(string section, out PWSTR values);

	/// <summary>Reads all section names.</summary>
	/// <param name="sectionNames">Receives the allocated multistring.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT GetSectionNames(out PWSTR sectionNames);

	/// <summary>Writes a PROPVARIANT value.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="key">The value name.</param>
	/// <param name="flags">The write flags.</param>
	/// <param name="value">The value to write.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT WritePropVariant(string section, string key, uint flags, in PROPVARIANT value);

	/// <summary>Writes a string value.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="key">The value name.</param>
	/// <param name="flags">The write flags.</param>
	/// <param name="value">The value to write.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT WriteString(string section, string key, uint flags, string? value);

	/// <summary>Writes all values in a section.</summary>
	/// <param name="section">The profile section.</param>
	/// <param name="values">The multistring values to write.</param>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT WriteSection(string section, string? values);

	/// <summary>Flushes the current cache entry.</summary>
	/// <returns>The HRESULT returned by the profile.</returns>
	[PreserveSig]
	HRESULT FlushEntry();
}

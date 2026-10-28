// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Native declarations preserve Windows SDK namespaces.

using System;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Windows.Win32;

/// <summary>Provides helpers for working with COM objects.</summary>
public static class ComHelpers
{
	/// <summary>Attempts to view a native object as the requested COM interface.</summary>
	/// <typeparam name="TInterface">The interface type to request.</typeparam>
	/// <param name="nativeObject">The object to cast.</param>
	/// <param name="instance">Receives the cast object when the interface is available.</param>
	/// <returns><see cref="HRESULT.S_OK"/> when the cast succeeds; otherwise <see cref="HRESULT.E_NOINTERFACE"/>.</returns>
	public static HRESULT TryCast<TInterface>(object nativeObject, out TInterface? instance)
		where TInterface : class
	{
		instance = null;

		if (nativeObject is not TInterface casted)
		{
			return HRESULT.E_NOINTERFACE;
		}

		instance = casted;

		return HRESULT.S_OK;
	}
}

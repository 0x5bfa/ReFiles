// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace Files.Core.Storage.Windows;

[GeneratedComClass]
internal sealed unsafe partial class WindowsShellContextMenuSite : IOleWindow
{
	private readonly HWND _owner;

	internal WindowsShellContextMenuSite(HWND owner)
	{
		_owner = owner;
	}

	/// <inheritdoc />
	public HRESULT GetWindow(HWND* window)
	{
		if (window is null)
		{
			return HRESULT.E_POINTER;
		}

		*window = _owner;

		return HRESULT.S_OK;
	}

	/// <inheritdoc />
	public HRESULT ContextSensitiveHelp(BOOL enterMode)
	{
		return HRESULT.E_NOTIMPL;
	}
}

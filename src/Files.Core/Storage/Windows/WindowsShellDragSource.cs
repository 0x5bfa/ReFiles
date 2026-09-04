// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Contains an apartment-neutral Windows Shell selection prepared for a WinUI drag surface.
/// </summary>
public sealed class WindowsShellDragSource
{
	private const WindowsShellDropEffects TransferEffects = WindowsShellDropEffects.Copy | WindowsShellDropEffects.Move | WindowsShellDropEffects.Link;
	private const SFGAO_FLAGS TransferAttributes = SFGAO_FLAGS.SFGAO_CANCOPY | SFGAO_FLAGS.SFGAO_CANMOVE | SFGAO_FLAGS.SFGAO_CANLINK;

	private readonly IReadOnlyList<WindowsItemLocator> _locators;

	internal WindowsShellDragSource(IReadOnlyList<WindowsItemLocator> locators)
	{
		ArgumentNullException.ThrowIfNull(locators);

		_locators = locators;
	}

	/// <summary>
	/// Attaches the native Shell data object to a WinRT data package on the calling STA.
	/// </summary>
	/// <param name="provider">The native data-object provider obtained from the WinRT data package at the UI surface.</param>
	/// <param name="ownerWindowHandle">The window that owns the drag selection.</param>
	/// <returns>The operations supported by every item in the selection.</returns>
	public WindowsShellDropEffects Attach(IDataObjectProvider provider, nint ownerWindowHandle)
	{
		return Attach(provider, ownerWindowHandle, WindowsShellDropEffects.None, deriveMoveFromDelete: true);
	}

	/// <summary>
	/// Attaches a native Shell data object to a WinUI data package while applying surface-specific transfer policy.
	/// </summary>
	/// <param name="provider">The native data-object provider obtained from the WinRT data package at the UI surface.</param>
	/// <param name="ownerWindowHandle">The window that owns the drag selection.</param>
	/// <param name="preferredEffect">The optional operation preferred by the initiating Shell surface.</param>
	/// <param name="deriveMoveFromDelete">Whether Shell delete capability should also advertise a move operation.</param>
	/// <returns>The operations supported by every item in the selection.</returns>
	public WindowsShellDropEffects Attach(IDataObjectProvider provider, nint ownerWindowHandle, WindowsShellDropEffects preferredEffect, bool deriveMoveFromDelete)
	{
		ArgumentNullException.ThrowIfNull(provider);

		var allowedEffects = GetAllowedEffects(deriveMoveFromDelete);
		var dataObject = WindowsShellDataObjectFactory.Create(_locators, (HWND)ownerWindowHandle);
		var preferredTransferEffect = preferredEffect & TransferEffects;
		if (preferredTransferEffect is not WindowsShellDropEffects.None)
		{
			_ = WindowsShellDataObjectFormat.TrySetDword(dataObject, WindowsShellDataObjectFormat.PreferredDropEffect, (uint)preferredTransferEffect);
		}

		provider.SetDataObject(dataObject).ThrowOnFailure();

		return allowedEffects;
	}

	internal static WindowsShellDropEffects MapAllowedEffects(SFGAO_FLAGS attributes, bool deriveMoveFromDelete)
	{
		if (deriveMoveFromDelete && attributes.HasFlag(SFGAO_FLAGS.SFGAO_CANDELETE))
		{
			attributes |= SFGAO_FLAGS.SFGAO_CANMOVE;
		}

		return (WindowsShellDropEffects)((uint)attributes & (uint)TransferEffects);
	}

	internal static SFGAO_FLAGS GetRequestedAttributes(bool deriveMoveFromDelete)
	{
		return deriveMoveFromDelete ? TransferAttributes | SFGAO_FLAGS.SFGAO_CANDELETE : TransferAttributes;
	}

	private WindowsShellDropEffects GetAllowedEffects(bool deriveMoveFromDelete)
	{
		var shellItems = WindowsShellItemArrayFactory.Create(_locators);
		var requestedAttributes = GetRequestedAttributes(deriveMoveFromDelete);
		var hr = shellItems.GetAttributes(SIATTRIBFLAGS.SIATTRIBFLAGS_AND, requestedAttributes, out var attributes);
		if (hr.Failed)
		{
			return WindowsShellDropEffects.None;
		}

		return MapAllowedEffects(attributes, deriveMoveFromDelete);
	}
}

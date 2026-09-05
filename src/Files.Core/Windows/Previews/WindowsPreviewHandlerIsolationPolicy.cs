// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Buffers.Binary;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Files.Core.Windows;

internal sealed class WindowsPreviewHandlerIsolationPolicy : IWindowsPreviewHandlerIsolationPolicy
{
	private const string DisableLowIntegrityValue = "DisableLowILProcessIsolation";

	public bool RequiresLowIntegrity(Guid handlerClsid)
	{
		var keyPath = $"Software\\Classes\\CLSID\\{handlerClsid:B}";
		if (TryReadOptOut(RegistryView.Registry64, keyPath, out var disabled) || TryReadOptOut(RegistryView.Registry32, keyPath, out disabled))
		{
			return !disabled;
		}

		return true;
	}

	internal static bool IsLowIntegrityDisabled(object? value, RegistryValueKind valueKind)
	{
		if (valueKind is RegistryValueKind.DWord && value is int numericValue)
		{
			return numericValue is not 0;
		}

		return valueKind is RegistryValueKind.Binary && value is byte[] { Length: sizeof(uint) } bytes && BinaryPrimitives.ReadUInt32LittleEndian(bytes) is not 0;
	}

	private static bool TryReadOptOut(RegistryView view, string keyPath, out bool disabled)
	{
		disabled = false;
		RegistryKey? key;
		try
		{
			using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
			key = localMachine.OpenSubKey(keyPath, writable: false);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
		{
			return false;
		}

		if (key is null)
		{
			return false;
		}

		using (key)
		{
			try
			{
				var value = key.GetValue(DisableLowIntegrityValue);
				disabled = value is not null && IsLowIntegrityDisabled(value, key.GetValueKind(DisableLowIntegrityValue));
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
			{
				disabled = false;
			}
		}

		return true;
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;

namespace Files.Core.Windows;

[GeneratedComClass]
internal sealed partial class WindowsShellCommandStorePropertyBag : IPropertyBag
{
	private const string CommandStoreRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";
	private const int AccessDeniedResult = unchecked((int)0x80070005);
	private const int TypeElementNotFoundResult = unchecked((int)0x8002802B);
	private readonly IReadOnlyDictionary<string, object> _values;

	private WindowsShellCommandStorePropertyBag(IReadOnlyDictionary<string, object> values)
	{
		_values = values;
	}

	/// <inheritdoc />
	public HRESULT Read(PCWSTR propertyName, ref ComVariant value, IErrorLog errorLog)
	{
		if (!_values.TryGetValue(propertyName.ToString(), out var storedValue))
		{
			return new(TypeElementNotFoundResult);
		}

		try
		{
			value.Dispose();
			value = storedValue switch
			{
				string stringValue => ComVariant.Create(stringValue),
				int integerValue => ComVariant.Create(integerValue),
				long longValue => ComVariant.Create(longValue),
				_ => default,
			};

			return value.VarType is VarEnum.VT_EMPTY ? new HRESULT(TypeElementNotFoundResult) : HRESULT.S_OK;
		}
		catch (ArgumentException)
		{
			return new(TypeElementNotFoundResult);
		}
	}

	/// <inheritdoc />
	public HRESULT Write(PCWSTR propertyName, in ComVariant value)
	{
		return new(AccessDeniedResult);
	}

	internal static WindowsShellCommandStorePropertyBag? TryCreate(string commandId)
	{
		try
		{
			using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using var commandKey = baseKey.OpenSubKey($@"{CommandStoreRegistryPath}\{commandId}");
			if (commandKey is null)
			{
				return null;
			}

			var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (var valueName in commandKey.GetValueNames())
			{
				if (commandKey.GetValue(valueName) is { } registryValue)
				{
					values[valueName] = registryValue;
				}
			}

			return new(values);
		}
		catch (Exception exception) when (exception is IOException or System.Security.SecurityException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}

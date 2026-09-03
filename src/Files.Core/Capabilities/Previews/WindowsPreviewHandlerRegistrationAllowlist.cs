// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Files.Core.Capabilities.Previews;

/// <summary>Checks the per-user and machine-wide Windows Shell preview handler registrations.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPreviewHandlerRegistrationAllowlist : IWindowsPreviewHandlerRegistrationAllowlist
{
	private const string PreviewHandlersRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers";

	private readonly Func<RegistryHive, string, bool> _registrationLookup;

	/// <summary>Gets the shared registration allowlist.</summary>
	public static WindowsPreviewHandlerRegistrationAllowlist Instance { get; } = new();

	/// <summary>Initializes a Windows preview handler registration allowlist.</summary>
	public WindowsPreviewHandlerRegistrationAllowlist()
		: this(IsRegistryValueDefined)
	{
	}

	internal WindowsPreviewHandlerRegistrationAllowlist(Func<RegistryHive, string, bool> registrationLookup)
	{
		ArgumentNullException.ThrowIfNull(registrationLookup);

		_registrationLookup = registrationLookup;
	}

	/// <inheritdoc />
	public bool IsRegistered(Guid handlerClsid)
	{
		if (handlerClsid == Guid.Empty)
		{
			throw new ArgumentException("A preview handler CLSID is required.", nameof(handlerClsid));
		}

		var valueName = handlerClsid.ToString("B");

		return IsRegistered(RegistryHive.CurrentUser, valueName) || IsRegistered(RegistryHive.LocalMachine, valueName);
	}

	private bool IsRegistered(RegistryHive hive, string valueName)
	{
		try
		{
			return _registrationLookup(hive, valueName);
		}
		catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool IsRegistryValueDefined(RegistryHive hive, string valueName)
	{
		using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
		using var previewHandlersKey = baseKey.OpenSubKey(PreviewHandlersRegistryPath);
		if (previewHandlersKey is null)
		{
			return false;
		}

		return IsRegistrationValueKind(previewHandlersKey.GetValueKind(valueName));
	}

	internal static bool IsRegistrationValueKind(RegistryValueKind valueKind) => valueKind is RegistryValueKind.String;
}

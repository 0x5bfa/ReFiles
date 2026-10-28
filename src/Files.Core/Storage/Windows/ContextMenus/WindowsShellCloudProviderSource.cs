// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

internal static unsafe class WindowsShellCloudProviderSource
{
	internal static WindowsShellCloudProviderInfo? GetProviderForSelection(IReadOnlyList<string> selectionPaths)
	{
		ArgumentNullException.ThrowIfNull(selectionPaths);

		if (selectionPaths.Count is 0 || selectionPaths.Any(string.IsNullOrWhiteSpace))
		{
			return null;
		}

		try
		{
			var activationResult = PInvoke.CoCreateInstance(typeof(SyncRootManagerCoclass).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out ISyncRootManager? manager);
			if (activationResult.Failed || manager is null)
			{
				return null;
			}

			IStorageProviderInfo? providerInfo = null;
			foreach (var path in selectionPaths)
			{
				var lookupResult = manager.GetStorageProviderInfoFromPath(path, out var candidate, null, null);
				if (lookupResult.Failed || candidate is null)
				{
					return null;
				}

				if (providerInfo is null)
				{
					providerInfo = candidate;
				}
				else if (providerInfo.IsSameIdentifier(candidate) != HRESULT.S_OK)
				{
					return null;
				}
			}

			if (providerInfo is null)
			{
				return null;
			}

			var verbsResult = providerInfo.GetContextMenuVerbs(out var verbCount, out var verbClassIds, out _);
			Guid[] cloudFileVerbClassIds;
			try
			{
				if (verbsResult.Failed || (verbCount is not 0 && verbClassIds is null))
				{
					return null;
				}

				cloudFileVerbClassIds = verbCount is 0 ? [] : new ReadOnlySpan<Guid>(verbClassIds, checked((int)verbCount)).ToArray();
			}
			finally
			{
				if (verbClassIds is not null)
				{
					PInvoke.CoTaskMemFree(verbClassIds);
				}
			}

			var packageFullName = string.Empty;
			string? logoPath = null;
			string? displayName = null;
			var iconResult = providerInfo.GetIcon(out var iconPathValue, out var iconIndex);
			var iconPath = ReadCoTaskMemString(iconResult, iconPathValue);
			if (providerInfo is IStorageProviderAppInfo appInfo)
			{
				var packageNameResult = appInfo.GetPackageFullName(out var packageNameValue);
				packageFullName = ReadCoTaskMemString(packageNameResult, packageNameValue) ?? string.Empty;
				var logoPathResult = appInfo.GetLogoPath(out var logoPathValue);
				logoPath = ReadCoTaskMemString(logoPathResult, logoPathValue);
				var displayNameResult = appInfo.GetAppDisplayName(out var displayNameValue);
				displayName = ReadCoTaskMemString(displayNameResult, displayNameValue);
			}

			if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
			{
				var displayNameResult = providerInfo.GetDisplayName(out var displayNameValue);
				displayName = ReadCoTaskMemString(displayNameResult, displayNameValue);
			}

			return new(packageFullName, displayName, logoPath, iconPath, iconIndex, cloudFileVerbClassIds);
		}
		catch (Exception exception) when (exception is COMException or Win32Exception)
		{
			return null;
		}
	}

	private static string? ReadCoTaskMemString(HRESULT result, PWSTR value)
	{
		var text = result.Succeeded && value.Value is not null ? value.ToString() : null;
		if (value.Value is not null)
		{
			PInvoke.CoTaskMemFree(value.Value);
		}

		return string.IsNullOrWhiteSpace(text) ? null : text;
	}
}

internal sealed record WindowsShellCloudProviderInfo(string PackageFullName, string? DisplayName, string? LogoPath, string? IconPath, int IconIndex, IReadOnlyList<Guid> CloudFileVerbClassIds);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Files.Core.Capabilities;
using OwlCore.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.Urlmon;
using Windows.Win32.UI.Shell;

namespace Files.Core.Windows;

internal sealed class WindowsPreviewUrlTrustResolver : IWindowsPreviewTrustResolver
{
	private const int FileNotFoundHResult = unchecked((int)0x80070002);
	private const uint InternetZone = 3;

	public WindowsPreviewTrustResult GetTrust(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.CoreModel is not IWindowsStorable item || context.CoreModel is not IFile || string.IsNullOrWhiteSpace(item.ParsingName))
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		try
		{
			var hr = PInvoke.SHCreateItemFromParsingName(item.ParsingName, null, out IShellItem shellItem);
			if (hr != HRESULT.S_OK)
			{
				return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
			}

			var url = ShellItemHelpers.TryGetDisplayName(shellItem, SIGDN.SIGDN_URL);

			return url is null ? new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate) : EvaluateUrlPolicy(url, item.FileSystemPath);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or NotSupportedException or SecurityException)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}
	}

	private static WindowsPreviewTrustResult EvaluateUrlPolicy(string url, string? fileSystemPath)
	{
		try
		{
			var zoneCheckHr = PInvoke.ZoneCheckUrlExCache(url, out var zonePolicy, sizeof(uint), 0, 0, PInvoke.URLACTION_SHELL_PREVIEW, (uint)PUAF.PUAF_NOUI, null, 0);

			return InterpretZoneCheckPolicy(zoneCheckHr, zonePolicy);
		}
		catch (EntryPointNotFoundException)
		{
		}
		catch (DllNotFoundException)
		{
		}

		if (!string.IsNullOrWhiteSpace(fileSystemPath))
		{
			try
			{
				var alternateDataStreamHr = PInvoke.GetZoneFromAlternateDataStreamEx(fileSystemPath, out var alternateDataStreamZone);
				if (alternateDataStreamHr == HRESULT.S_OK && alternateDataStreamZone >= InternetZone)
				{
					return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Blocked);
				}

				if (alternateDataStreamHr != HRESULT.S_OK && alternateDataStreamHr.Value != FileNotFoundHResult)
				{
					return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
				}
			}
			catch (EntryPointNotFoundException)
			{
			}
			catch (DllNotFoundException)
			{
			}
		}

		if (PInvoke.CoInternetCreateSecurityManager(null!, out var securityManager, 0) != HRESULT.S_OK)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		var policy = new byte[sizeof(uint)];
		byte context = 0;
		var hr = securityManager.ProcessUrlAction(url, PInvoke.URLACTION_SHELL_PREVIEW, policy, in context, 0, (uint)PUAF.PUAF_NOUI, 0);

		return InterpretUrlPolicy(hr, policy);
	}

	internal static WindowsPreviewTrustResult InterpretUrlPolicy(HRESULT hr, ReadOnlySpan<byte> policy)
	{
		if (hr != HRESULT.S_OK || policy.Length < sizeof(uint))
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		return InterpretUrlPolicy(hr, BinaryPrimitives.ReadUInt32LittleEndian(policy));
	}

	internal static WindowsPreviewTrustResult InterpretZoneCheckPolicy(HRESULT hr, uint policy)
	{
		if (hr.Failed)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		return new WindowsPreviewTrustResult(policy == PInvoke.URLPOLICY_ALLOW ? WindowsPreviewTrustStatus.Allowed : WindowsPreviewTrustStatus.Blocked);
	}

	internal static WindowsPreviewTrustResult InterpretUrlPolicy(HRESULT hr, uint policy)
	{
		if (hr != HRESULT.S_OK)
		{
			return new WindowsPreviewTrustResult(WindowsPreviewTrustStatus.Indeterminate);
		}

		return InterpretPolicy(policy);
	}

	private static WindowsPreviewTrustResult InterpretPolicy(uint policy)
	{
		var permissions = policy & PInvoke.URLPOLICY_MASK_PERMISSIONS;

		return new WindowsPreviewTrustResult(permissions == PInvoke.URLPOLICY_ALLOW ? WindowsPreviewTrustStatus.Allowed : WindowsPreviewTrustStatus.Blocked);
	}
}

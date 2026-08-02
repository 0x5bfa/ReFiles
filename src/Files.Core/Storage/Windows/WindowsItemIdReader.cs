// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Uses the filesystem volume and file index when available and otherwise keeps an explicit address fallback.
/// </summary>
[SupportedOSPlatform("windows6.0.6000")]
internal sealed class WindowsItemIdReader : IWindowsItemIdReader
{
	private const string FileIdentityPrefix = "winfs:v1:";
	private const string AddressPrefix = "winshell-address:v1:";
	private const FileOptions BackupSemantics = (FileOptions)0x02000000;

	public string GetItemId(IShellItem shellItem, string parsingName, string? fileSystemPath)
	{
		ArgumentNullException.ThrowIfNull(shellItem);
		ArgumentException.ThrowIfNullOrWhiteSpace(parsingName);

		if (fileSystemPath is not null && TryGetFileId(fileSystemPath, out var fileId))
		{
			if (fileId.NumberOfLinks <= 1)
			{
				return $"{FileIdentityPrefix}{fileId.VolumeSerialNumber:X8}:{fileId.FileIndex:X16}";
			}

			// A file ID identifies the shared file object, while the parsing name
			// identifies the directory entry that Shell enumerated.
			return CreateAddressIdentity(parsingName);
		}

		return CreateAddressIdentity(parsingName);
	}

	public bool TryGetParsingName(string itemId, out string parsingName)
	{
		parsingName = string.Empty;

		if (!itemId.StartsWith(AddressPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		return TryDecodeAddress(itemId[AddressPrefix.Length..], out parsingName);
	}

	public bool IsFileSystemIdentity(string itemId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
		return itemId.StartsWith(FileIdentityPrefix, StringComparison.Ordinal);
	}

	private static bool TryGetFileId(string fileSystemPath, out WindowsFileId fileId)
	{
		fileId = default;

		try
		{
			using var handle = File.OpenHandle(fileSystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BackupSemantics);

			if (handle.IsInvalid
				|| !PInvoke.GetFileInformationByHandle(handle, out var information))
			{
				return false;
			}

			fileId = new WindowsFileId(
				information.dwVolumeSerialNumber,
				((ulong)information.nFileIndexHigh << 32) | information.nFileIndexLow,
				information.nNumberOfLinks);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static string CreateAddressIdentity(string parsingName)
	{
		return $"{AddressPrefix}{EncodeAddress(parsingName)}";
	}

	private static string EncodeAddress(string parsingName)
	{
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(parsingName))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');
	}

	private static bool TryDecodeAddress(string encodedAddress, out string parsingName)
	{
		parsingName = string.Empty;

		if (string.IsNullOrWhiteSpace(encodedAddress))
		{
			return false;
		}

		try
		{
			var paddedAddress = encodedAddress
				.Replace('-', '+')
				.Replace('_', '/');
			paddedAddress = paddedAddress.PadRight(paddedAddress.Length + ((4 - paddedAddress.Length % 4) % 4), '=');
			parsingName = Encoding.UTF8.GetString(Convert.FromBase64String(paddedAddress));
			return !string.IsNullOrWhiteSpace(parsingName);
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private readonly record struct WindowsFileId(uint VolumeSerialNumber, ulong FileIndex, uint NumberOfLinks);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Reads filesystem attribute capabilities and changes NTFS compression or encryption state.
/// </summary>
public static unsafe class WindowsFileAttributeService
{
	private const uint FileSupportsEncryption = 0x00020000;

	/// <summary>
	/// Gets the advanced attribute capabilities of the volume containing an item.
	/// </summary>
	/// <param name="path">The filesystem item path.</param>
	/// <returns>The available compression and encryption capabilities.</returns>
	public static WindowsFileAttributeCapabilities GetCapabilities(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var root = Path.GetPathRoot(path);
		if (string.IsNullOrEmpty(root) || !PInvoke.GetVolumeInformation(root, [], out _, out _, out var flags, []))
		{
			return default;
		}

		return new((flags & PInvoke.FILE_FILE_COMPRESSION) is not 0, (flags & FileSupportsEncryption) is not 0);
	}

	/// <summary>
	/// Changes the per-file or per-directory compression state.
	/// </summary>
	/// <param name="path">The filesystem item path.</param>
	/// <param name="compress">Whether compression should be enabled.</param>
	public static void SetCompression(string path, bool compress)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		using var handle = PInvoke.CreateFile(path, (uint)FILE_ACCESS_RIGHTS.FILE_WRITE_DATA,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE, null,
			FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS, null);
		if (handle.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}

		var format = compress ? COMPRESSION_FORMAT.COMPRESSION_FORMAT_DEFAULT : COMPRESSION_FORMAT.COMPRESSION_FORMAT_NONE;
		ReadOnlySpan<byte> input = MemoryMarshal.AsBytes(new ReadOnlySpan<COMPRESSION_FORMAT>(in format));
		if (!PInvoke.DeviceIoControl(handle, PInvoke.FSCTL_SET_COMPRESSION, input, [], out _, null))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}
	}

	/// <summary>
	/// Changes the EFS encryption state of a file or directory.
	/// </summary>
	/// <param name="path">The filesystem item path.</param>
	/// <param name="encrypt">Whether encryption should be enabled.</param>
	public static void SetEncryption(string path, bool encrypt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var succeeded = encrypt ? PInvoke.EncryptFile(path) : PInvoke.DecryptFile(path);
		if (!succeeded)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError());
		}
	}
}

/// <summary>
/// Describes advanced filesystem attribute capabilities.
/// </summary>
public readonly record struct WindowsFileAttributeCapabilities
{
	/// <summary>Gets a value indicating whether per-file compression is supported.</summary>
	public bool SupportsCompression { get; }

	/// <summary>Gets a value indicating whether EFS encryption is supported.</summary>
	public bool SupportsEncryption { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsFileAttributeCapabilities"/> structure.
	/// </summary>
	/// <param name="supportsCompression">Whether per-file compression is supported.</param>
	/// <param name="supportsEncryption">Whether EFS encryption is supported.</param>
	public WindowsFileAttributeCapabilities(bool supportsCompression, bool supportsEncryption)
	{
		SupportsCompression = supportsCompression;
		SupportsEncryption = supportsEncryption;
	}
}

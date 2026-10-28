// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

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

	/// <summary>Applies selected attributes to items and optionally their descendants.</summary>
	/// <param name="paths">The item paths.</param>
	/// <param name="readOnly">The read-only state, or null to preserve it.</param>
	/// <param name="hidden">The hidden state, or null to preserve it.</param>
	/// <param name="archive">The archive state, or null to preserve it.</param>
	/// <param name="indexed">The indexing state, or null to preserve it.</param>
	/// <param name="compressed">The compression state, or null to preserve it.</param>
	/// <param name="encrypted">The encryption state, or null to preserve it.</param>
	/// <param name="applyToContents">Whether to update descendants without following reparse points.</param>
	/// <param name="cancellationToken">The cooperative cancellation token.</param>
	public static void ApplyChanges(IReadOnlyList<string> paths, bool? readOnly, bool? hidden, bool? archive, bool? indexed, bool? compressed, bool? encrypted, bool applyToContents,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(paths);

		if (readOnly is null && hidden is null && archive is null && indexed is null && compressed is null && encrypted is null)
		{
			return;
		}

		foreach (var path in paths)
		{
			cancellationToken.ThrowIfCancellationRequested();

			ApplyItemChanges(path, readOnly, hidden, archive, indexed, compressed, encrypted);
			if (applyToContents && Directory.Exists(path))
			{
				var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
				foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();

					ApplyItemChanges(entry, readOnly, hidden, archive, indexed, compressed, encrypted);
				}
			}
		}
	}

	private static void ApplyItemChanges(string path, bool? readOnly, bool? hidden, bool? archive, bool? indexed, bool? compressed, bool? encrypted)
	{
		if (readOnly.HasValue || hidden.HasValue || archive.HasValue || indexed.HasValue)
		{
			var attributes = File.GetAttributes(path);
			attributes = SetAttribute(attributes, FileAttributes.ReadOnly, readOnly);
			attributes = SetAttribute(attributes, FileAttributes.Hidden, hidden);
			attributes = SetAttribute(attributes, FileAttributes.Archive, archive);
			attributes = SetAttribute(attributes, FileAttributes.NotContentIndexed, indexed is null ? null : !indexed.Value);
			File.SetAttributes(path, attributes);
		}

		if (compressed is false)
		{
			SetCompression(path, false);
		}

		if (encrypted is false)
		{
			SetEncryption(path, false);
		}

		if (compressed is true)
		{
			SetCompression(path, true);
		}

		if (encrypted is true)
		{
			SetEncryption(path, true);
		}
	}

	private static FileAttributes SetAttribute(FileAttributes attributes, FileAttributes attribute, bool? value)
	{
		return value is null ? attributes : value.Value ? attributes | attribute : attributes & ~attribute;
	}
}

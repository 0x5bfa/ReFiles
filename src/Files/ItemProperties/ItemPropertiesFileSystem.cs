// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable RS0030 // Physical filesystem access is isolated behind this adapter.

using System.IO;
using System.Runtime.InteropServices;
using Files.Core.Storage.Windows;
using Windows.Win32;

namespace Files.ItemProperties;

internal interface IItemPropertiesFileSystem
{
	ItemPropertiesFileSystemSelection Inspect(IReadOnlyList<ItemPropertiesFileSystemCandidate> candidates);

	ItemPropertiesFileSystemMetadata ReadMetadata(IReadOnlyList<string> paths, CancellationToken cancellationToken);

	void Apply(IReadOnlyList<string> paths, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken);
}

internal sealed class ItemPropertiesFileSystem : IItemPropertiesFileSystem
{
	private const uint DriveCdRom = 5;
	private const uint FileReadOnlyVolume = 0x00080000;

	public ItemPropertiesFileSystemSelection Inspect(IReadOnlyList<ItemPropertiesFileSystemCandidate> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);

		var paths = new List<string>(candidates.Count);
		var hasFolders = false;
		foreach (var candidate in candidates)
		{
			var exists = candidate.IsFolder ? Directory.Exists(candidate.Path) : File.Exists(candidate.Path);
			if (!exists)
			{
				continue;
			}

			paths.Add(candidate.Path);
			hasFolders |= candidate.IsFolder;
		}

		var isDrive = IsDriveRoot(paths);
		var isSingleFile = paths.Count is 1 && !hasFolders;
		var isSingleFolder = paths.Count is 1 && hasFolders && !isDrive;
		var isReadOnly = hasFolders ? null : GetCommonAttributeState(paths, FileAttributes.ReadOnly);
		var isHidden = GetCommonAttributeState(paths, FileAttributes.Hidden);
		var isArchive = GetCommonAttributeState(paths, FileAttributes.Archive);
		var isIndexed = Invert(GetCommonAttributeState(paths, FileAttributes.NotContentIndexed));
		var isCompressed = GetCommonAttributeState(paths, FileAttributes.Compressed);
		var isEncrypted = GetCommonAttributeState(paths, FileAttributes.Encrypted);
		var capabilities = GetCommonAttributeCapabilities(paths);

		return new(paths, hasFolders, isSingleFile, isSingleFolder, isDrive, isReadOnly, isHidden, isArchive, isIndexed, isCompressed, isEncrypted, capabilities);
	}

	public ItemPropertiesFileSystemMetadata ReadMetadata(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);

		if (IsDriveRoot(paths))
		{
			return new(0, 0, 0, 0, false, [], [], [], TryReadDrive(paths));
		}

		ulong size = 0;
		ulong sizeOnDisk = 0;
		var fileCount = 0;
		var folderCount = 0;
		var hasDirectory = false;
		var creationTimes = new List<DateTime>();
		var modifiedTimes = new List<DateTime>();
		var accessedTimes = new List<DateTime>();
		var clusterSizes = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
		foreach (var path in paths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var attributes = File.GetAttributes(path);
			if (attributes.HasFlag(FileAttributes.Directory))
			{
				hasDirectory = true;
				var info = new DirectoryInfo(path);
				creationTimes.Add(info.CreationTime);
				modifiedTimes.Add(info.LastWriteTime);
				accessedTimes.Add(info.LastAccessTime);
				ReadDirectoryContents(path, clusterSizes, ref size, ref sizeOnDisk, ref fileCount, ref folderCount, cancellationToken);
			}
			else
			{
				var info = new FileInfo(path);
				var length = checked((ulong)info.Length);
				size = checked(size + length);
				sizeOnDisk = checked(sizeOnDisk + GetSizeOnDisk(info, clusterSizes));
				fileCount++;
				creationTimes.Add(info.CreationTime);
				modifiedTimes.Add(info.LastWriteTime);
				accessedTimes.Add(info.LastAccessTime);
			}
		}

		return new(size, sizeOnDisk, fileCount, folderCount, hasDirectory, creationTimes, modifiedTimes, accessedTimes, null);
	}

	public void Apply(IReadOnlyList<string> paths, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(paths);

		ArgumentNullException.ThrowIfNull(changes);

		if (changes.IsDrive)
		{
			ApplyDriveChanges(paths[0], changes, cancellationToken);

			return;
		}

		foreach (var path in paths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ApplyAttributes(path, changes.IsReadOnly, changes.IsHidden, changes.IsArchive, changes.IsIndexed);
			ApplyAdvancedAttributes(path, changes.IsCompressed, changes.IsEncrypted, changes.UpdateCompression, changes.UpdateEncryption);
			if (changes.ApplyToContents && Directory.Exists(path))
			{
				var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
				foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					ApplyAttributes(entry, changes.IsReadOnly, changes.IsHidden, changes.IsArchive, changes.IsIndexed);
					ApplyAdvancedAttributes(entry, changes.IsCompressed, changes.IsEncrypted, changes.UpdateCompression, changes.UpdateEncryption);
				}
			}
		}
	}

	private static bool? GetCommonAttributeState(IReadOnlyList<string> paths, FileAttributes attribute)
	{
		bool? common = null;
		foreach (var path in paths)
		{
			bool value;
			try
			{
				value = File.GetAttributes(path).HasFlag(attribute);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return null;
			}

			if (common is not null && common.Value != value)
			{
				return null;
			}

			common = value;
		}

		return common;
	}

	private static WindowsFileAttributeCapabilities GetCommonAttributeCapabilities(IReadOnlyList<string> paths)
	{
		var supportsCompression = paths.Count is not 0;
		var supportsEncryption = paths.Count is not 0;
		foreach (var path in paths)
		{
			var capabilities = WindowsFileAttributeService.GetCapabilities(path);
			supportsCompression &= capabilities.SupportsCompression;
			supportsEncryption &= capabilities.SupportsEncryption;
		}

		return new(supportsCompression, supportsEncryption);
	}

	private static bool? Invert(bool? value)
	{
		return value is null ? null : !value.Value;
	}

	private static void ReadDirectoryContents(string path, Dictionary<string, uint> clusterSizes, ref ulong size, ref ulong sizeOnDisk, ref int fileCount, ref int folderCount,
		CancellationToken cancellationToken)
	{
		var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
		foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var attributes = File.GetAttributes(entry);
			if (attributes.HasFlag(FileAttributes.Directory))
			{
				folderCount++;
				continue;
			}

			var info = new FileInfo(entry);
			var length = checked((ulong)info.Length);
			size = checked(size + length);
			sizeOnDisk = checked(sizeOnDisk + GetSizeOnDisk(info, clusterSizes));
			fileCount++;
		}
	}

	private static uint GetClusterSize(string path, Dictionary<string, uint> clusterSizes)
	{
		var root = Path.GetPathRoot(path);
		if (string.IsNullOrWhiteSpace(root))
		{
			return 0;
		}

		if (clusterSizes.TryGetValue(root, out var clusterSize))
		{
			return clusterSize;
		}

		if (PInvoke.GetDiskFreeSpace(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _))
		{
			clusterSize = checked(sectorsPerCluster * bytesPerSector);
		}

		clusterSizes[root] = clusterSize;

		return clusterSize;
	}

	private static ulong GetSizeOnDisk(FileInfo info, Dictionary<string, uint> clusterSizes)
	{
		if (info.Attributes.HasFlag(FileAttributes.Compressed) || info.Attributes.HasFlag(FileAttributes.SparseFile))
		{
			Marshal.SetLastPInvokeError(0);
			var low = PInvoke.GetCompressedFileSize(info.FullName, out var high);
			var error = Marshal.GetLastPInvokeError();
			if (low != uint.MaxValue || error is 0)
			{
				return ((ulong)high << 32) | low;
			}
		}

		var length = checked((ulong)info.Length);
		var clusterSize = GetClusterSize(info.FullName, clusterSizes);
		if (length is 0 || clusterSize is 0)
		{
			return length;
		}

		return checked(((length + clusterSize - 1) / clusterSize) * clusterSize);
	}

	private static ItemPropertiesDriveMetadata? TryReadDrive(IReadOnlyList<string> paths)
	{
		if (!IsDriveRoot(paths))
		{
			return null;
		}

		var root = Path.GetPathRoot(paths[0])!;
		try
		{
			var drive = new DriveInfo(root);
			if (!drive.IsReady)
			{
				return null;
			}

			var capacity = checked((ulong)drive.TotalSize);
			var freeSpace = checked((ulong)drive.TotalFreeSpace);
			var attributes = File.GetAttributes(root);
			PInvoke.GetVolumeInformation(root, [], out _, out _, out var fileSystemFlags, []);
			var driveType = PInvoke.GetDriveType(root);
			var isReadOnly = (fileSystemFlags & FileReadOnlyVolume) is not 0;

			return new(
				drive.VolumeLabel,
				drive.DriveFormat,
				checked(capacity - freeSpace),
				freeSpace,
				capacity,
				attributes.HasFlag(FileAttributes.Compressed),
				!attributes.HasFlag(FileAttributes.NotContentIndexed),
				(fileSystemFlags & PInvoke.FILE_FILE_COMPRESSION) is not 0 && !isReadOnly,
				driveType is not DriveCdRom && !isReadOnly,
				driveType is not DriveCdRom && !isReadOnly,
				WindowsShellStorageSettingsService.SupportsDriveUsage(root));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool IsDriveRoot(IReadOnlyList<string> paths)
	{
		if (paths.Count is not 1 || !Directory.Exists(paths[0]))
		{
			return false;
		}

		var root = Path.GetPathRoot(paths[0]);

		return !string.IsNullOrWhiteSpace(root) && Path.TrimEndingDirectorySeparator(paths[0]).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
	}

	private static void ApplyDriveChanges(string rootPath, ItemPropertiesFileSystemChanges changes, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (changes.IsDriveIndexed != changes.InitialIsDriveIndexed)
		{
			ApplyAttributes(rootPath, null, null, null, changes.IsDriveIndexed);
		}

		if (changes.IsDriveCompressed != changes.InitialIsDriveCompressed)
		{
			WindowsFileAttributeService.SetCompression(rootPath, changes.IsDriveCompressed);
		}

		if (!changes.ApplyToContents)
		{
			return;
		}

		var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
		foreach (var entry in Directory.EnumerateFileSystemEntries(rootPath, "*", options))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (changes.IsDriveIndexed != changes.InitialIsDriveIndexed)
			{
				ApplyAttributes(entry, null, null, null, changes.IsDriveIndexed);
			}

			if (changes.IsDriveCompressed != changes.InitialIsDriveCompressed)
			{
				WindowsFileAttributeService.SetCompression(entry, changes.IsDriveCompressed);
			}
		}
	}

	private static void ApplyAdvancedAttributes(string path, bool? compressed, bool? encrypted, bool updateCompression, bool updateEncryption)
	{
		if (updateCompression && compressed is false)
		{
			WindowsFileAttributeService.SetCompression(path, false);
		}

		if (updateEncryption && encrypted is false)
		{
			WindowsFileAttributeService.SetEncryption(path, false);
		}

		if (updateCompression && compressed is true)
		{
			WindowsFileAttributeService.SetCompression(path, true);
		}

		if (updateEncryption && encrypted is true)
		{
			WindowsFileAttributeService.SetEncryption(path, true);
		}
	}

	private static void ApplyAttributes(string path, bool? readOnly, bool? hidden, bool? archive, bool? indexed)
	{
		var attributes = File.GetAttributes(path);
		attributes = SetAttribute(attributes, FileAttributes.ReadOnly, readOnly);
		attributes = SetAttribute(attributes, FileAttributes.Hidden, hidden);
		attributes = SetAttribute(attributes, FileAttributes.Archive, archive);
		attributes = SetAttribute(attributes, FileAttributes.NotContentIndexed, Invert(indexed));
		File.SetAttributes(path, attributes);
	}

	private static FileAttributes SetAttribute(FileAttributes attributes, FileAttributes attribute, bool? value)
	{
		if (value is null)
		{
			return attributes;
		}

		return value.Value ? attributes | attribute : attributes & ~attribute;
	}
}

internal sealed record ItemPropertiesFileSystemCandidate(string Path, bool IsFolder);

internal sealed record ItemPropertiesFileSystemSelection(IReadOnlyList<string> Paths, bool HasFolders, bool IsSingleFile, bool IsSingleFolder, bool IsDrive, bool? IsReadOnly, bool? IsHidden,
	bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, WindowsFileAttributeCapabilities Capabilities);

internal sealed record ItemPropertiesFileSystemMetadata(ulong Size, ulong SizeOnDisk, int FileCount, int FolderCount, bool HasDirectory, IReadOnlyList<DateTime> CreationTimes,
	IReadOnlyList<DateTime> ModifiedTimes, IReadOnlyList<DateTime> AccessedTimes, ItemPropertiesDriveMetadata? Drive);

internal sealed record ItemPropertiesDriveMetadata(string VolumeLabel, string FileSystem, ulong UsedSpace, ulong FreeSpace, ulong Capacity, bool IsCompressed, bool IsIndexed, bool SupportsCompression,
	bool SupportsIndexing, bool CanRename, bool SupportsStorageDetails);

internal sealed record ItemPropertiesFileSystemChanges(bool IsDrive, bool InitialIsDriveCompressed, bool InitialIsDriveIndexed, bool IsDriveCompressed, bool IsDriveIndexed,
	bool? IsReadOnly, bool? IsHidden, bool? IsArchive, bool? IsIndexed, bool? IsCompressed, bool? IsEncrypted, bool UpdateCompression, bool UpdateEncryption, bool ApplyToContents);

#pragma warning restore RS0030

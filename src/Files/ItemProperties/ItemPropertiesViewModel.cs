// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Storage.Windows;
using Files.Localization;
using Files.ViewModels;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Win32;

namespace Files.ItemProperties;

internal sealed class ItemPropertiesViewModel : ObservableObject
{
	private const string FileDescriptionPropertyId = "System.FileDescription";
	private readonly IReadOnlyList<BrowseItemViewModel> _items;
	private readonly List<string> _fileSystemPaths;
	private readonly string _hiddenFileExtension;
	private bool? _initialIsReadOnly;
	private bool? _initialIsHidden;
	private bool? _initialIsArchive;
	private string _originalName;
	private string _name;
	private string _windowTitle;
	private string _description;
	private string _size;
	private string _sizeOnDisk;
	private string _contains;
	private string _dateCreated;
	private string _dateModified;
	private string _dateAccessed;
	private string _fileSystem;
	private string _usedSpace;
	private string _freeSpace;
	private string _capacity;
	private bool? _isReadOnly;
	private bool? _isHidden;
	private bool? _isArchive;
	private bool _applyToContents;
	private bool _isLoading;
	private bool _isDrive;
	private bool _isInitialized;
	private BitmapImage? _icon;

	public string WindowTitle
	{
		get => _windowTitle;
		private set => SetProperty(ref _windowTitle, value);
	}

	public string Name
	{
		get => _name;
		set
		{
			if (SetProperty(ref _name, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public string Type { get; }

	public string Description
	{
		get => _description;
		private set => SetProperty(ref _description, value);
	}

	public string Location { get; }

	public BitmapImage? Icon
	{
		get => _icon;
		private set
		{
			if (SetProperty(ref _icon, value))
			{
				OnPropertyChanged(nameof(HasIcon));
			}
		}
	}

	public bool HasIcon => Icon is not null;

	public string Size
	{
		get => _size;
		private set => SetProperty(ref _size, value);
	}

	public string SizeOnDisk
	{
		get => _sizeOnDisk;
		private set => SetProperty(ref _sizeOnDisk, value);
	}

	public string Contains
	{
		get => _contains;
		private set => SetProperty(ref _contains, value);
	}

	public string DateCreated
	{
		get => _dateCreated;
		private set => SetProperty(ref _dateCreated, value);
	}

	public string DateModified
	{
		get => _dateModified;
		private set => SetProperty(ref _dateModified, value);
	}

	public string DateAccessed
	{
		get => _dateAccessed;
		private set => SetProperty(ref _dateAccessed, value);
	}

	public string FileSystem
	{
		get => _fileSystem;
		private set => SetProperty(ref _fileSystem, value);
	}

	public string UsedSpace
	{
		get => _usedSpace;
		private set => SetProperty(ref _usedSpace, value);
	}

	public string FreeSpace
	{
		get => _freeSpace;
		private set => SetProperty(ref _freeSpace, value);
	}

	public string Capacity
	{
		get => _capacity;
		private set => SetProperty(ref _capacity, value);
	}

	public bool? IsReadOnly
	{
		get => _isReadOnly;
		set
		{
			if (SetProperty(ref _isReadOnly, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool? IsHidden
	{
		get => _isHidden;
		set
		{
			if (SetProperty(ref _isHidden, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool? IsArchive
	{
		get => _isArchive;
		set
		{
			if (SetProperty(ref _isArchive, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool ApplyToContents
	{
		get => _applyToContents;
		set => SetProperty(ref _applyToContents, value);
	}

	public bool IsLoading
	{
		get => _isLoading;
		private set => SetProperty(ref _isLoading, value);
	}

	public bool IsDrive
	{
		get => _isDrive;
		private set => SetProperty(ref _isDrive, value);
	}

	public bool CanRename => _items.Count is 1 && _fileSystemPaths.Count is 1 && !_isDrive;

	public bool CanEditAttributes => _fileSystemPaths.Count == _items.Count && !_isDrive;

	public bool HasFolders => _fileSystemPaths.Any(Directory.Exists);

	public bool HasChanges => CanEditAttributes
		&& (!StringComparer.Ordinal.Equals(Name, _originalName) || IsReadOnly != _initialIsReadOnly || IsHidden != _initialIsHidden || IsArchive != _initialIsArchive);

	public string GeneralLabel { get; } = Strings.General.GetLocalized();

	public string DetailsLabel { get; } = Strings.Details.GetLocalized();

	public string NameLabel { get; } = Strings.Name.GetLocalized();

	public string TypeLabel { get; } = FormatPropertyLabel(Strings.TypeOfFile.GetLocalized());

	public string DescriptionLabel { get; } = FormatPropertyLabel(Strings.Description.GetLocalized());

	public string LocationLabel { get; } = FormatPropertyLabel(Strings.Location.GetLocalized());

	public string SizeLabel { get; } = FormatPropertyLabel(Strings.Size.GetLocalized());

	public string SizeOnDiskLabel { get; } = FormatPropertyLabel(Strings.SizeOnDisk.GetLocalized());

	public string ContainsLabel { get; } = FormatPropertyLabel(Strings.Contains.GetLocalized());

	public string DateCreatedLabel { get; } = FormatPropertyLabel(Strings.DateCreated.GetLocalized());

	public string DateModifiedLabel { get; } = FormatPropertyLabel(Strings.DateModified.GetLocalized());

	public string DateAccessedLabel { get; } = FormatPropertyLabel(Strings.DateAccessed.GetLocalized());

	public string AttributesLabel { get; } = FormatPropertyLabel(Strings.Attributes.GetLocalized());

	public string ReadOnlyLabel { get; } = Strings.ReadOnly.GetLocalized();

	public string HiddenLabel { get; } = Strings.Hidden.GetLocalized();

	public string ArchiveLabel { get; } = Strings.Archive.GetLocalized();

	public string ApplyToContentsLabel { get; } = Strings.ApplyAttributesToContents.GetLocalized();

	public string AdvancedLabel { get; } = Strings.Advanced.GetLocalized();

	public string AdvancedAttributesLabel { get; } = Strings.AdvancedAttributes.GetLocalized();

	public string FileSystemLabel { get; } = FormatPropertyLabel(Strings.FileSystem.GetLocalized());

	public string UsedSpaceLabel { get; } = FormatPropertyLabel(Strings.UsedSpace.GetLocalized());

	public string FreeSpaceLabel { get; } = FormatPropertyLabel(Strings.FreeSpace.GetLocalized());

	public string CapacityLabel { get; } = FormatPropertyLabel(Strings.Capacity.GetLocalized());

	public string OkLabel { get; } = Strings.Ok.GetLocalized();

	public string CancelLabel { get; } = Strings.Cancel.GetLocalized();

	public string ApplyLabel { get; } = Strings.Apply.GetLocalized();

	public ObservableCollection<ItemPropertyDetail> Details { get; }

	public ItemPropertiesViewModel(IReadOnlyList<BrowseItemViewModel> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (items.Count is 0)
		{
			throw new ArgumentException("At least one item is required.", nameof(items));
		}

		_items = items;
		_fileSystemPaths = GetFileSystemPaths(items);
		var unspecified = Strings.Unspecified.GetLocalized();
		_name = items.Count is 1 ? items[0].DisplayName : FormatItemCount(items.Count);
		_originalName = _name;
		_hiddenFileExtension = items.Count is 1 ? GetHiddenFileExtension(items[0]) : string.Empty;
		_windowTitle = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesTitleFormat.GetLocalized(), items.Count is 1 ? items[0].DisplayName : _name);
		Type = items.Count is 1 ? FormatType(items[0]) : Strings.MultipleTypes.GetLocalized();
		_description = items.Count is 1 ? GetValue(items[0], FileDescriptionPropertyId, unspecified) : unspecified;
		Location = GetLocation(items) ?? unspecified;
		_icon = items.Count is 1 ? items[0].Thumbnail : null;
		_size = GetSize(items) ?? unspecified;
		_sizeOnDisk = _size;
		_contains = unspecified;
		_dateCreated = items.Count is 1 ? GetValue(items[0], BrowseDisplayPropertyIds.DateCreated, unspecified) : unspecified;
		_dateModified = items.Count is 1 ? GetValue(items[0], BrowseDisplayPropertyIds.DateModified, unspecified) : unspecified;
		_dateAccessed = unspecified;
		_fileSystem = unspecified;
		_usedSpace = unspecified;
		_freeSpace = unspecified;
		_capacity = unspecified;
		_initialIsReadOnly = GetCommonAttributeState(_fileSystemPaths, FileAttributes.ReadOnly);
		_initialIsHidden = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Hidden);
		_initialIsArchive = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Archive);
		_isReadOnly = _initialIsReadOnly;
		_isHidden = _initialIsHidden;
		_isArchive = _initialIsArchive;
		_isDrive = IsDriveRoot(_fileSystemPaths);
		Details = new(BuildDetails(items));
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_isInitialized || _fileSystemPaths.Count is 0)
		{
			return;
		}

		_isInitialized = true;
		IsLoading = true;
		try
		{
			var metadata = await Task.Run(() => ReadFileSystemMetadata(_fileSystemPaths, cancellationToken), cancellationToken);
			Size = FormatSize(metadata.Size);
			SizeOnDisk = FormatSize(metadata.SizeOnDisk);
			Contains = metadata.HasDirectory ? string.Format(CultureInfo.CurrentCulture, Strings.ContainsFormat.GetLocalized(), metadata.FileCount, metadata.FolderCount) : Strings.Unspecified.GetLocalized();
			DateCreated = FormatCommonDate(metadata.CreationTimes);
			DateModified = FormatCommonDate(metadata.ModifiedTimes);
			DateAccessed = FormatCommonDate(metadata.AccessedTimes);
			if (metadata.Drive is { } drive)
			{
				IsDrive = true;
				FileSystem = drive.FileSystem;
				UsedSpace = FormatSize(drive.UsedSpace);
				FreeSpace = FormatSize(drive.FreeSpace);
				Capacity = FormatSize(drive.Capacity);
			}
		}
		finally
		{
			IsLoading = false;
		}
	}

	public async Task ApplyAsync(CancellationToken cancellationToken = default)
	{
		if (!HasChanges)
		{
			return;
		}

		ValidateName();
		var requestedName = Name;
		var requestedReadOnly = IsReadOnly;
		var requestedHidden = IsHidden;
		var requestedArchive = IsArchive;
		var applyToContents = ApplyToContents;
		await Task.Run(() => ApplyChanges(requestedName, requestedReadOnly, requestedHidden, requestedArchive, applyToContents, cancellationToken), cancellationToken);
		_originalName = Name;
		_initialIsReadOnly = IsReadOnly;
		_initialIsHidden = IsHidden;
		_initialIsArchive = IsArchive;
		WindowTitle = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesTitleFormat.GetLocalized(), Name);
		OnPropertyChanged(nameof(HasChanges));
	}

	internal void SetGeneralShellProperties(string? description, BitmapImage? icon)
	{
		if (!string.IsNullOrWhiteSpace(description))
		{
			Description = description;
		}

		if (icon is not null)
		{
			Icon = icon;
		}
	}

	internal void SetShellDetails(IReadOnlyList<WindowsShellPropertyValue> details)
	{
		ArgumentNullException.ThrowIfNull(details);

		if (details.Count is 0)
		{
			return;
		}

		Details.Clear();
		foreach (var detail in details)
		{
			Details.Add(new(detail.Name, detail.Value));
		}
	}

	private static string FormatItemCount(int count)
	{
		var format = count is 1 ? Strings.ItemCountSingle.GetLocalized() : Strings.ItemCountPlural.GetLocalized();

		return string.Format(CultureInfo.CurrentCulture, format, count);
	}

	private static string FormatPropertyLabel(string label)
	{
		return string.Format(CultureInfo.CurrentCulture, Strings.PropertyLabelFormat.GetLocalized(), label);
	}

	private static string GetValue(BrowseItemViewModel item, string propertyId, string fallback)
	{
		var value = item.GetDisplayText(propertyId);

		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	private static string FormatType(BrowseItemViewModel item)
	{
		var type = GetValue(item, BrowseDisplayPropertyIds.Type, item.Kind);
		var extension = item.IsFolder ? string.Empty : Path.GetExtension(item.Name);
		if (string.IsNullOrWhiteSpace(extension) || type.Contains(extension, StringComparison.OrdinalIgnoreCase))
		{
			return type;
		}

		return $"{type} ({extension.ToLowerInvariant()})";
	}

	private static string GetHiddenFileExtension(BrowseItemViewModel item)
	{
		if (item.IsFolder || StringComparer.Ordinal.Equals(item.Name, item.DisplayName))
		{
			return string.Empty;
		}

		var extension = Path.GetExtension(item.Name);

		return extension.Length is not 0 && StringComparer.Ordinal.Equals(item.Name[..^extension.Length], item.DisplayName) ? extension : string.Empty;
	}

	private static string? GetLocation(IReadOnlyList<BrowseItemViewModel> items)
	{
		string? location = null;
		foreach (var item in items)
		{
			var value = item.Reference.LastKnownAddress?.Value;
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			var itemLocation = Path.IsPathRooted(value) ? Path.GetDirectoryName(value) : value;
			if (string.IsNullOrWhiteSpace(itemLocation))
			{
				return null;
			}

			if (location is not null && !string.Equals(location, itemLocation, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			location = itemLocation;
		}

		return location;
	}

	private static string? GetSize(IReadOnlyList<BrowseItemViewModel> items)
	{
		ulong total = 0;
		foreach (var item in items)
		{
			if (!item.Properties.TryGetValue(BrowseDisplayPropertyIds.Size, out var value) || !TryGetUInt64(value, out var size))
			{
				return null;
			}

			total = checked(total + size);
		}

		return FormatSize(total);
	}

	private static bool TryGetUInt64(object? value, out ulong result)
	{
		try
		{
			result = Convert.ToUInt64(value, CultureInfo.InvariantCulture);

			return true;
		}
		catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
		{
			result = 0;

			return false;
		}
	}

	private static string FormatSize(ulong size)
	{
		string[] suffixes =
		[
			Strings.ByteSymbol.GetLocalized(),
			Strings.KilobyteSymbol.GetLocalized(),
			Strings.MegabyteSymbol.GetLocalized(),
			Strings.GigabyteSymbol.GetLocalized(),
			Strings.TerabyteSymbol.GetLocalized(),
			Strings.PetabyteSymbol.GetLocalized(),
		];
		var value = (double)size;
		var suffixIndex = 0;
		while (value >= 1024 && suffixIndex < suffixes.Length - 1)
		{
			value /= 1024;
			suffixIndex++;
		}

		if (suffixIndex is 0)
		{
			return string.Format(CultureInfo.CurrentCulture, Strings.SizeBytesFormat.GetLocalized(), size, suffixes[suffixIndex]);
		}

		var scaledFormat = value < 10 ? "N2" : value < 100 ? "N1" : "N0";

		return string.Format(CultureInfo.CurrentCulture, Strings.SizeScaledFormat.GetLocalized(), value.ToString(scaledFormat, CultureInfo.CurrentCulture), suffixes[suffixIndex], size, suffixes[0]);
	}

	private static List<string> GetFileSystemPaths(IReadOnlyList<BrowseItemViewModel> items)
	{
		var paths = new List<string>(items.Count);
		foreach (var item in items)
		{
			var address = item.Reference.LastKnownAddress;
			if (address is null || !address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(address.Value))
			{
				continue;
			}

			if (File.Exists(address.Value) || Directory.Exists(address.Value))
			{
				paths.Add(address.Value);
			}
		}

		return paths;
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

	private static IReadOnlyList<ItemPropertyDetail> BuildDetails(IReadOnlyList<BrowseItemViewModel> items)
	{
		var propertyIds = items.SelectMany(static item => item.Properties.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
		var details = new List<ItemPropertyDetail>(propertyIds.Length);
		foreach (var propertyId in propertyIds)
		{
			var values = items.Select(item => item.Properties.TryGetValue(propertyId, out var value) ? FormatPropertyValue(value) : string.Empty).ToArray();
			var value = values.All(candidate => StringComparer.CurrentCulture.Equals(candidate, values[0])) ? values[0] : Strings.MultipleValues.GetLocalized();
			if (!string.IsNullOrWhiteSpace(value))
			{
				details.Add(new ItemPropertyDetail(HumanizePropertyName(propertyId), value));
			}
		}

		return details;
	}

	private static string FormatPropertyValue(object? value)
	{
		return value switch
		{
			null => string.Empty,
			DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("g", CultureInfo.CurrentCulture),
			DateTime dateTime => dateTime.ToString("g", CultureInfo.CurrentCulture),
			IEnumerable<string> values => string.Join(", ", values),
			IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty,
			_ => value.ToString() ?? string.Empty,
		};
	}

	private static string HumanizePropertyName(string propertyId)
	{
		var name = propertyId[(propertyId.LastIndexOf('.') + 1)..];
		if (name.Length is 0)
		{
			return propertyId;
		}

		var characters = new List<char>(name.Length + 8) { name[0] };
		for (var index = 1; index < name.Length; index++)
		{
			if (char.IsUpper(name[index]) && !char.IsUpper(name[index - 1]))
			{
				characters.Add(' ');
			}

			characters.Add(name[index]);
		}

		return new string([.. characters]);
	}

	private static FileSystemMetadata ReadFileSystemMetadata(IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		if (IsDriveRoot(paths))
		{
			return new FileSystemMetadata(0, 0, 0, 0, false, [], [], [], TryReadDrive(paths));
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

		return new FileSystemMetadata(size, sizeOnDisk, fileCount, folderCount, hasDirectory, creationTimes, modifiedTimes, accessedTimes, null);
	}

	private static void ReadDirectoryContents(
		string path,
		Dictionary<string, uint> clusterSizes,
		ref ulong size,
		ref ulong sizeOnDisk,
		ref int fileCount,
		ref int folderCount,
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

	private static DriveMetadata? TryReadDrive(IReadOnlyList<string> paths)
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

			return new DriveMetadata(drive.DriveFormat, checked(capacity - freeSpace), freeSpace, capacity);
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

	private static string FormatCommonDate(IReadOnlyList<DateTime> values)
	{
		if (values.Count is 0 || values.Any(value => value != values[0]))
		{
			return Strings.Unspecified.GetLocalized();
		}

		var value = values[0];

		return $"{value.ToString("D", CultureInfo.CurrentCulture)}, {value.ToString("T", CultureInfo.CurrentCulture)}";
	}

	private void ValidateName()
	{
		if (!CanRename || StringComparer.Ordinal.Equals(Name, _originalName))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(Name) || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !StringComparer.Ordinal.Equals(Path.GetFileName(Name), Name))
		{
			throw new ArgumentException(Strings.InvalidFileName.GetLocalized(), nameof(Name));
		}
	}

	private void ApplyChanges(string requestedName, bool? requestedReadOnly, bool? requestedHidden, bool? requestedArchive, bool applyToContents, CancellationToken cancellationToken)
	{
		foreach (var path in _fileSystemPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ApplyAttributes(path, requestedReadOnly, requestedHidden, requestedArchive);
			if (applyToContents && Directory.Exists(path))
			{
				var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
				foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					ApplyAttributes(entry, requestedReadOnly, requestedHidden, requestedArchive);
				}
			}
		}

		if (CanRename && !StringComparer.Ordinal.Equals(requestedName, _originalName))
		{
			var sourcePath = _fileSystemPaths[0];
			var parentPath = Path.GetDirectoryName(sourcePath) ?? throw new IOException(Strings.ItemParentUnavailable.GetLocalized());
			var destinationPath = Path.Combine(parentPath, requestedName + _hiddenFileExtension);
			if (Directory.Exists(sourcePath))
			{
				Directory.Move(sourcePath, destinationPath);
			}
			else
			{
				File.Move(sourcePath, destinationPath);
			}

			_fileSystemPaths[0] = destinationPath;
		}
	}

	private static void ApplyAttributes(string path, bool? readOnly, bool? hidden, bool? archive)
	{
		var attributes = File.GetAttributes(path);
		attributes = SetAttribute(attributes, FileAttributes.ReadOnly, readOnly);
		attributes = SetAttribute(attributes, FileAttributes.Hidden, hidden);
		attributes = SetAttribute(attributes, FileAttributes.Archive, archive);
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

	private sealed record FileSystemMetadata(
		ulong Size, ulong SizeOnDisk, int FileCount, int FolderCount, bool HasDirectory, IReadOnlyList<DateTime> CreationTimes, IReadOnlyList<DateTime> ModifiedTimes,
		IReadOnlyList<DateTime> AccessedTimes, DriveMetadata? Drive);

	private sealed record DriveMetadata(string FileSystem, ulong UsedSpace, ulong FreeSpace, ulong Capacity);
}

internal sealed record ItemPropertyDetail(string Name, string Value);

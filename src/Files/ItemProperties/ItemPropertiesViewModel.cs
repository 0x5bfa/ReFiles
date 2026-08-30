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
using Microsoft.UI.Xaml.Media;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

internal sealed class ItemPropertiesViewModel : ObservableObject
{
	private const uint DriveCdRom = 5;
	private const uint FileReadOnlyVolume = 0x00080000;
	private const string FileDescriptionPropertyId = "System.FileDescription";
	private readonly IReadOnlyList<BrowseItemViewModel> _items;
	private readonly List<string> _fileSystemPaths;
	private readonly string _hiddenFileExtension;
	private bool? _initialIsReadOnly;
	private bool? _initialIsHidden;
	private bool? _initialIsArchive;
	private bool? _initialIsIndexed;
	private bool? _initialIsCompressed;
	private bool? _initialIsEncrypted;
	private bool _initialIsDriveCompressed;
	private bool _initialIsDriveIndexed;
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
	private double _usedPercentage;
	private bool _isDriveCompressed;
	private bool _isDriveIndexed;
	private bool? _isReadOnly;
	private bool? _isHidden;
	private bool? _isArchive;
	private bool? _isIndexed;
	private bool? _isCompressed;
	private bool? _isEncrypted;
	private bool _applyToContents;
	private bool _supportsCompression;
	private bool _supportsEncryption;
	private bool _supportsDriveIndexing;
	private bool _supportsDriveStorageDetails;
	private bool _canRenameDrive;
	private bool _isLoading;
	private bool _isDrive;
	private bool _isInitialized;
	private bool _hasPropertyPageChanges;
	private ImageSource? _icon;

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

	public ImageSource? Icon
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

	internal string? PrimaryPath => _fileSystemPaths.Count is 1 ? _fileSystemPaths[0] : null;

	internal bool RequiresAttributeScopeSelection => HasFolders && (CanEditAttributes && HasPendingFileAttributeChanges || IsDrive && HasPendingDriveAttributeChanges);

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

	public double UsedPercentage
	{
		get => _usedPercentage;
		private set => SetProperty(ref _usedPercentage, value);
	}

	public bool IsDriveCompressed
	{
		get => _isDriveCompressed;
		set
		{
			if (SetProperty(ref _isDriveCompressed, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool IsDriveIndexed
	{
		get => _isDriveIndexed;
		set
		{
			if (SetProperty(ref _isDriveIndexed, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
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

	public bool? IsIndexed
	{
		get => _isIndexed;
		set
		{
			if (SetProperty(ref _isIndexed, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool? IsCompressed
	{
		get => _isCompressed;
		set
		{
			if (SetProperty(ref _isCompressed, value))
			{
				OnPropertyChanged(nameof(HasChanges));
			}
		}
	}

	public bool? IsEncrypted
	{
		get => _isEncrypted;
		set
		{
			if (SetProperty(ref _isEncrypted, value))
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

	public bool SupportsCompression
	{
		get => _supportsCompression;
		private set => SetProperty(ref _supportsCompression, value);
	}

	public bool SupportsEncryption
	{
		get => _supportsEncryption;
		private set => SetProperty(ref _supportsEncryption, value);
	}

	public bool CanRename => _items.Count is 1 && _fileSystemPaths.Count is 1 && (!_isDrive || _canRenameDrive);

	public bool CanEditAttributes => _fileSystemPaths.Count == _items.Count && !_isDrive;

	public bool ShowDriveCompression => _isDrive && SupportsCompression;

	public bool ShowDriveIndexing => _isDrive && _supportsDriveIndexing;

	public bool ShowDriveStorageDetails => _isDrive && _supportsDriveStorageDetails;

	public bool CanEditDriveCompression => ShowDriveCompression && _canRenameDrive;

	public bool CanEditDriveIndexing => ShowDriveIndexing && _canRenameDrive;

	public bool HasFolders => _fileSystemPaths.Any(Directory.Exists);

	public bool IsSingleFile => _fileSystemPaths.Count is 1 && File.Exists(_fileSystemPaths[0]);

	public bool IsSingleFolder => _fileSystemPaths.Count is 1 && Directory.Exists(_fileSystemPaths[0]) && !_isDrive;

	public bool HasChanges => HasGeneralChanges || _hasPropertyPageChanges;

	private bool HasGeneralChanges => CanRename && !StringComparer.Ordinal.Equals(Name, _originalName)
		|| CanEditAttributes && HasPendingFileAttributeChanges || IsDrive && HasPendingDriveAttributeChanges;

	private bool HasPendingDriveAttributeChanges => IsDriveCompressed != _initialIsDriveCompressed || IsDriveIndexed != _initialIsDriveIndexed;

	private bool HasPendingFileAttributeChanges => IsReadOnly != _initialIsReadOnly || IsHidden != _initialIsHidden || IsArchive != _initialIsArchive
		|| IsIndexed != _initialIsIndexed || IsCompressed != _initialIsCompressed || IsEncrypted != _initialIsEncrypted;

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

	public string ReadOnlyLabel => IsSingleFolder ? Strings.ReadOnlyFolder.GetLocalized() : Strings.ReadOnly.GetLocalized();

	public string FileSystemLabel { get; } = FormatPropertyLabel(Strings.FileSystem.GetLocalized());

	public string UsedSpaceLabel { get; } = FormatPropertyLabel(Strings.UsedSpace.GetLocalized());

	public string FreeSpaceLabel { get; } = FormatPropertyLabel(Strings.FreeSpace.GetLocalized());

	public string CapacityLabel { get; } = FormatPropertyLabel(Strings.Capacity.GetLocalized());

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
		_usedPercentage = 0;
		_isDriveCompressed = false;
		_isDriveIndexed = false;
		_isDrive = IsDriveRoot(_fileSystemPaths);
		_initialIsReadOnly = HasFolders ? null : GetCommonAttributeState(_fileSystemPaths, FileAttributes.ReadOnly);
		_initialIsHidden = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Hidden);
		_initialIsArchive = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Archive);
		_initialIsIndexed = Invert(GetCommonAttributeState(_fileSystemPaths, FileAttributes.NotContentIndexed));
		_initialIsCompressed = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Compressed);
		_initialIsEncrypted = GetCommonAttributeState(_fileSystemPaths, FileAttributes.Encrypted);
		_isReadOnly = _initialIsReadOnly;
		_isHidden = _initialIsHidden;
		_isArchive = _initialIsArchive;
		_isIndexed = _initialIsIndexed;
		_isCompressed = _initialIsCompressed;
		_isEncrypted = _initialIsEncrypted;
		var capabilities = GetCommonAttributeCapabilities(_fileSystemPaths);
		_supportsCompression = capabilities.SupportsCompression;
		_supportsEncryption = capabilities.SupportsEncryption;
		_canRenameDrive = !_isDrive;
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
				if (StringComparer.Ordinal.Equals(Name, _originalName))
				{
					Name = drive.VolumeLabel;
					_originalName = Name;
				}

				var driveDisplayName = WindowsVolumeLabelService.GetDisplayName(_fileSystemPaths[0]);
				WindowTitle = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesTitleFormat.GetLocalized(), driveDisplayName);
				FileSystem = drive.FileSystem;
				UsedSpace = FormatSize(drive.UsedSpace);
				FreeSpace = FormatSize(drive.FreeSpace);
				Capacity = FormatSize(drive.Capacity);
				UsedPercentage = drive.Capacity is 0 ? 0 : drive.UsedSpace * 100d / drive.Capacity;
				IsDriveCompressed = drive.IsCompressed;
				IsDriveIndexed = drive.IsIndexed;
				_initialIsDriveCompressed = drive.IsCompressed;
				_initialIsDriveIndexed = drive.IsIndexed;
				SupportsCompression = drive.SupportsCompression;
				_supportsDriveIndexing = drive.SupportsIndexing;
				_supportsDriveStorageDetails = drive.SupportsStorageDetails;
				_canRenameDrive = drive.CanRename;
				OnPropertyChanged(nameof(CanRename));
				OnPropertyChanged(nameof(ShowDriveCompression));
				OnPropertyChanged(nameof(ShowDriveIndexing));
				OnPropertyChanged(nameof(ShowDriveStorageDetails));
				OnPropertyChanged(nameof(CanEditDriveCompression));
				OnPropertyChanged(nameof(CanEditDriveIndexing));
				OnPropertyChanged(nameof(HasChanges));
			}
		}
		finally
		{
			IsLoading = false;
		}
	}

	public async Task ApplyAsync(CancellationToken cancellationToken = default)
	{
		await ApplyAsync(default, cancellationToken);
	}

	internal async Task ApplyAsync(HWND owner, CancellationToken cancellationToken = default)
	{
		if (!HasGeneralChanges)
		{
			return;
		}

		ValidateName();
		var requestedName = Name;
		var requestedReadOnly = IsReadOnly;
		var requestedHidden = IsHidden;
		var requestedArchive = IsArchive;
		var requestedIndexed = IsIndexed;
		var requestedCompressed = IsCompressed;
		var requestedEncrypted = IsEncrypted;
		var updateCompression = IsCompressed is not null && IsCompressed != _initialIsCompressed;
		var updateEncryption = IsEncrypted is not null && IsEncrypted != _initialIsEncrypted;
		var applyToContents = ApplyToContents;
		if (IsDrive && !StringComparer.Ordinal.Equals(requestedName, _originalName))
		{
			WindowsVolumeLabelService.SetLabel(owner, _fileSystemPaths[0], requestedName);
		}

		await Task.Run(
			() => ApplyChanges(requestedName, requestedReadOnly, requestedHidden, requestedArchive, requestedIndexed, requestedCompressed, requestedEncrypted,
				updateCompression, updateEncryption, applyToContents, cancellationToken), cancellationToken);
		_originalName = Name;
		_initialIsReadOnly = IsReadOnly;
		_initialIsHidden = IsHidden;
		_initialIsArchive = IsArchive;
		_initialIsIndexed = IsIndexed;
		_initialIsCompressed = IsCompressed;
		_initialIsEncrypted = IsEncrypted;
		_initialIsDriveCompressed = IsDriveCompressed;
		_initialIsDriveIndexed = IsDriveIndexed;
		var titleName = IsDrive ? WindowsVolumeLabelService.GetDisplayName(_fileSystemPaths[0]) : Name;
		WindowTitle = string.Format(CultureInfo.CurrentCulture, Strings.PropertiesTitleFormat.GetLocalized(), titleName);
		OnPropertyChanged(nameof(HasChanges));
	}

	internal void SetPropertyPageChanges(bool hasChanges)
	{
		if (_hasPropertyPageChanges == hasChanges)
		{
			return;
		}

		_hasPropertyPageChanges = hasChanges;
		OnPropertyChanged(nameof(HasChanges));
	}

	internal void SetGeneralShellProperties(string? description, ImageSource? icon)
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
			var attributes = File.GetAttributes(root);
			PInvoke.GetVolumeInformation(root, [], out _, out _, out var fileSystemFlags, []);
			var driveType = PInvoke.GetDriveType(root);
			var isReadOnly = (fileSystemFlags & FileReadOnlyVolume) is not 0;

			return new DriveMetadata(
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

		if (IsDrive)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(Name) || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !StringComparer.Ordinal.Equals(Path.GetFileName(Name), Name))
		{
			throw new ArgumentException(Strings.InvalidFileName.GetLocalized(), nameof(Name));
		}
	}

	private void ApplyChanges(string requestedName, bool? requestedReadOnly, bool? requestedHidden, bool? requestedArchive, bool? requestedIndexed,
		bool? requestedCompressed, bool? requestedEncrypted, bool updateCompression, bool updateEncryption, bool applyToContents, CancellationToken cancellationToken)
	{
		if (IsDrive)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var rootPath = _fileSystemPaths[0];
			if (IsDriveIndexed != _initialIsDriveIndexed)
			{
				ApplyAttributes(rootPath, null, null, null, IsDriveIndexed);
			}

			if (IsDriveCompressed != _initialIsDriveCompressed)
			{
				WindowsFileAttributeService.SetCompression(rootPath, IsDriveCompressed);
			}

			if (applyToContents)
			{
				var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
				foreach (var entry in Directory.EnumerateFileSystemEntries(rootPath, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (IsDriveIndexed != _initialIsDriveIndexed)
					{
						ApplyAttributes(entry, null, null, null, IsDriveIndexed);
					}

					if (IsDriveCompressed != _initialIsDriveCompressed)
					{
						WindowsFileAttributeService.SetCompression(entry, IsDriveCompressed);
					}
				}
			}

			return;
		}

		foreach (var path in _fileSystemPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ApplyAttributes(path, requestedReadOnly, requestedHidden, requestedArchive, requestedIndexed);
			ApplyAdvancedAttributes(path, requestedCompressed, requestedEncrypted, updateCompression, updateEncryption);
			if (applyToContents && Directory.Exists(path))
			{
				var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
				foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					ApplyAttributes(entry, requestedReadOnly, requestedHidden, requestedArchive, requestedIndexed);
					ApplyAdvancedAttributes(entry, requestedCompressed, requestedEncrypted, updateCompression, updateEncryption);
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

	private sealed record FileSystemMetadata(
		ulong Size, ulong SizeOnDisk, int FileCount, int FolderCount, bool HasDirectory, IReadOnlyList<DateTime> CreationTimes, IReadOnlyList<DateTime> ModifiedTimes,
		IReadOnlyList<DateTime> AccessedTimes, DriveMetadata? Drive);

	private sealed record DriveMetadata(string VolumeLabel, string FileSystem, ulong UsedSpace, ulong FreeSpace, ulong Capacity, bool IsCompressed, bool IsIndexed,
		bool SupportsCompression, bool SupportsIndexing, bool CanRename, bool SupportsStorageDetails);
}

internal sealed record ItemPropertyDetail(string Name, string Value);

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Files.Core.Storage;
using Files.Localization;
using Files.ViewModels;
using Files.Core.Windows;
using Microsoft.UI.Xaml.Media;
using Windows.Win32.Foundation;

namespace Files.ItemProperties;

internal sealed class ItemPropertiesViewModel : ObservableObject
{
	private const string FileDescriptionPropertyId = "System.FileDescription";
	private readonly IItemPropertiesFileSystem _itemPropertiesFileSystem;
	private readonly IReadOnlyList<BrowseItemViewModel> _items;
	private readonly List<string> _fileSystemPaths;
	private readonly string _hiddenFileExtension;
	private readonly IStorageOperationService? _storageOperations;
	private readonly bool _hasFolders;
	private readonly bool _isSingleFile;
	private readonly bool _isSingleFolder;
	private StorableReference? _renameReference;
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

	public bool HasFolders => _hasFolders;

	public bool IsSingleFile => _isSingleFile;

	public bool IsSingleFolder => _isSingleFolder;

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

	public ItemPropertiesViewModel(IReadOnlyList<BrowseItemViewModel> items) : this(items, null, new ItemPropertiesFileSystem())
	{
	}

	internal ItemPropertiesViewModel(IReadOnlyList<BrowseItemViewModel> items, IStorageOperationService? storageOperations, IItemPropertiesFileSystem fileSystem)
	{
		ArgumentNullException.ThrowIfNull(items);

		ArgumentNullException.ThrowIfNull(fileSystem);

		if (items.Count is 0)
		{
			throw new ArgumentException("At least one item is required.", nameof(items));
		}

		_itemPropertiesFileSystem = fileSystem;
		_items = items;
		_storageOperations = storageOperations;
		var selection = _itemPropertiesFileSystem.Inspect(GetFileSystemCandidates(items));
		_fileSystemPaths = [.. selection.Paths];
		_hasFolders = selection.HasFolders;
		_isSingleFile = selection.IsSingleFile;
		_isSingleFolder = selection.IsSingleFolder;
		_renameReference = items.Count is 1 ? items[0].Reference : null;
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
		_isDrive = selection.IsDrive;
		_initialIsReadOnly = selection.IsReadOnly;
		_initialIsHidden = selection.IsHidden;
		_initialIsArchive = selection.IsArchive;
		_initialIsIndexed = selection.IsIndexed;
		_initialIsCompressed = selection.IsCompressed;
		_initialIsEncrypted = selection.IsEncrypted;
		_isReadOnly = _initialIsReadOnly;
		_isHidden = _initialIsHidden;
		_isArchive = _initialIsArchive;
		_isIndexed = _initialIsIndexed;
		_isCompressed = _initialIsCompressed;
		_isEncrypted = _initialIsEncrypted;
		_supportsCompression = selection.Capabilities.SupportsCompression;
		_supportsEncryption = selection.Capabilities.SupportsEncryption;
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
			var metadata = await Task.Run(() => _itemPropertiesFileSystem.ReadMetadata(_fileSystemPaths, cancellationToken), cancellationToken);
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

		var changes = new ItemPropertiesFileSystemChanges(IsDrive, _initialIsDriveCompressed, _initialIsDriveIndexed, IsDriveCompressed, IsDriveIndexed, requestedReadOnly, requestedHidden,
			requestedArchive, requestedIndexed, requestedCompressed, requestedEncrypted, updateCompression, updateEncryption, applyToContents);
		await Task.Run(() => _itemPropertiesFileSystem.Apply(_fileSystemPaths, changes, cancellationToken), cancellationToken);
		if (!IsDrive && CanRename && !StringComparer.Ordinal.Equals(requestedName, _originalName))
		{
			await RenameAsync(requestedName + _hiddenFileExtension, cancellationToken);
		}

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

	private static IReadOnlyList<ItemPropertiesFileSystemCandidate> GetFileSystemCandidates(IReadOnlyList<BrowseItemViewModel> items)
	{
		var candidates = new List<ItemPropertiesFileSystemCandidate>(items.Count);
		foreach (var item in items)
		{
			var address = item.Reference.LastKnownAddress;
			if (address is null || !address.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(address.Value))
			{
				continue;
			}

			candidates.Add(new(address.Value, item.IsFolder));
		}

		return candidates;
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

	private async Task RenameAsync(string newName, CancellationToken cancellationToken)
	{
		if (_storageOperations is null || _renameReference is null)
		{
			throw new NotSupportedException("A storage operation service is required to rename this item.");
		}

		var result = await _storageOperations.ExecuteAsync(new RenameOperationRequest(_renameReference, newName), cancellationToken: cancellationToken);
		if (!result.Succeeded)
		{
			throw result.Error ?? new InvalidOperationException("The storage operation failed without an error.");
		}

		_renameReference = result.ResultItem ?? _renameReference;
		var resultAddress = result.ResultItem?.LastKnownAddress;
		if (resultAddress is not null && resultAddress.Scheme.Equals(WindowsStorageSource.FileAddressScheme, StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(resultAddress.Value))
		{
			_fileSystemPaths[0] = resultAddress.Value;

			return;
		}

		var parentPath = Path.GetDirectoryName(_fileSystemPaths[0]) ?? throw new IOException(Strings.ItemParentUnavailable.GetLocalized());
		_fileSystemPaths[0] = Path.Combine(parentPath, newName);
	}
}

internal sealed record ItemPropertyDetail(string Name, string Value);

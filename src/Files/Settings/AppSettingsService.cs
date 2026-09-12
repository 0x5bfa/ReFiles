// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Files.Controls;
using Files.Core.Storage;
using Files.Core.Windows;
using Windows.ApplicationModel;
using Windows.Globalization;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.Settings;

internal sealed partial class AppSettingsService : INotifyPropertyChanged, IDisposable
{
	private const string SettingsDirectoryName = "Settings";
	private const string SettingsFileName = "settings.json";
	private static readonly TimeSpan _defaultSaveDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _saveRetryDelay = TimeSpan.FromSeconds(5);

	private readonly Lock _syncRoot = new();
	private readonly WindowsStorageSource? _windowsSource;
	private readonly WindowsStorageOperationHandler? _windowsOperations;
	private readonly WindowsFolder? _localFolder;
	private readonly bool _ownsWindowsSource;
	private readonly TimeSpan _saveDelay;
	private AppSettingsData _settings;
	private Timer? _saveTimer;
	private bool _isDirty;
	private bool _isDisposed;

	[AppSetting("")]
	public partial string LanguageTag { get; set; }

	[AppSetting(true)]
	public partial bool ShowFileExtensions { get; set; }

	[AppSetting(false)]
	public partial bool ShowHiddenItems { get; set; }

	[AppSetting(AppThemeMode.System)]
	public partial AppThemeMode ThemeMode { get; set; }

	[AppSetting(320d, MinValue = 1d)]
	public partial double PreviewPaneWidth { get; set; }

	[AppSetting(false)]
	public partial bool IsPreviewPaneVisible { get; set; }

	[AppSetting(SidebarDisplayMode.Expanded)]
	public partial SidebarDisplayMode SidebarDisplayMode { get; set; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public AppSettingsService()
		: this(new WindowsStorageSource(), GetDefaultLocalFolderPath(), ownsWindowsSource: true, saveDelay: null)
	{
	}

	internal AppSettingsService(WindowsStorageSource source, TimeSpan? saveDelay = null)
		: this(source, GetDefaultLocalFolderPath(), ownsWindowsSource: false, saveDelay: saveDelay)
	{
	}

	internal AppSettingsService(string localFolderPath, TimeSpan? saveDelay = null)
		: this(new WindowsStorageSource(), localFolderPath, ownsWindowsSource: true, saveDelay: saveDelay)
	{
	}

	internal AppSettingsService(AppSettingsData settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		NormalizeSettings(settings);
		_settings = settings;
		_saveDelay = _defaultSaveDelay;
	}

	private AppSettingsService(WindowsStorageSource source, string localFolderPath, bool ownsWindowsSource, TimeSpan? saveDelay)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

		if (saveDelay is { } delay && delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(saveDelay));
		}

		_windowsSource = source;
		_windowsOperations = new WindowsStorageOperationHandler(source);
		_localFolder = ResolveFolder(source, Path.GetFullPath(localFolderPath));
		_ownsWindowsSource = ownsWindowsSource;
		_saveDelay = saveDelay ?? _defaultSaveDelay;
		_settings = new AppSettingsData();
		Load();
	}

	public void ApplyLanguage() => ApplicationLanguages.PrimaryLanguageOverride = LanguageTag;

	public void SaveNow()
	{
		lock (_syncRoot)
		{
			ThrowIfDisposed();
			SaveCore_NoLock();
		}
	}

	public void Dispose()
	{
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}

			try
			{
				SaveCore_NoLock();
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Failed to save application settings: {exception}");
			}

			_isDisposed = true;
			_saveTimer?.Dispose();
			_saveTimer = null;

			if (_ownsWindowsSource && _windowsSource is { } source)
			{
				try
				{
					source.DisposeAsync().AsTask().GetAwaiter().GetResult();
				}
				catch (Exception exception)
				{
					Debug.WriteLine($"Failed to dispose application settings storage: {exception}");
				}
			}
		}

		GC.SuppressFinalize(this);
	}

	private void Load()
	{
		var settingsFile = TryGetSettingsFile();
		if (settingsFile is null)
		{
			return;
		}

		try
		{
			using var stream = settingsFile.OpenStreamAsync(FileAccess.Read).GetAwaiter().GetResult();
			using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true);
			var json = reader.ReadToEnd();
			if (string.IsNullOrWhiteSpace(json))
			{
				return;
			}

			var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettingsData);
			if (loaded is null)
			{
				return;
			}

			NormalizeSettings(loaded);
			_settings = loaded;
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or COMException)
		{
			Debug.WriteLine($"Failed to load application settings: {exception}");
		}
	}

	private void SetValue<T>(string propertyName, T value, Func<AppSettingsData, T> getter, Action<AppSettingsData, T> setter)
	{
		lock (_syncRoot)
		{
			ThrowIfDisposed();
			if (EqualityComparer<T>.Default.Equals(getter(_settings), value))
			{
				return;
			}

			setter(_settings, value);
			_isDirty = true;
			QueueSave_NoLock();
		}

		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	private void QueueSave_NoLock(TimeSpan? delay = null)
	{
		if (_windowsSource is null)
		{
			return;
		}

		var saveTimer = _saveTimer ??= new Timer(static state => ((AppSettingsService)state!).SaveTimerElapsed(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		saveTimer.Change(delay ?? _saveDelay, Timeout.InfiniteTimeSpan);
	}

	private void SaveTimerElapsed()
	{
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}

			try
			{
				SaveCore_NoLock();
			}
			catch (Exception exception)
			{
				Debug.WriteLine($"Failed to save application settings: {exception}");
				if (_isDirty)
				{
					QueueSave_NoLock(_saveRetryDelay);
				}
			}
		}
	}

	private void SaveCore_NoLock()
	{
		if (!_isDirty || _windowsSource is null || _windowsOperations is null || _localFolder is null)
		{
			return;
		}

		var settingsFolder = EnsureSettingsFolder();
		var json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettingsData);
		var temporaryName = $"{SettingsFileName}.{Guid.NewGuid():N}.tmp";
		var temporaryFile = CreateTemporaryFile(settingsFolder, temporaryName);

		var committed = false;
		try
		{
			WriteText(temporaryFile, json);
			CommitTemporaryFile(settingsFolder, temporaryFile);
			_isDirty = false;
			committed = true;
		}
		finally
		{
			if (!committed)
			{
				TryDelete(temporaryFile);
			}
		}
	}

	private WindowsFolder EnsureSettingsFolder()
	{
		var localFolder = _localFolder ?? throw new InvalidOperationException("The Windows local folder is not configured.");
		if (TryGetSettingsFolder() is { } existingFolder)
		{
			return existingFolder;
		}

		try
		{
			var created = CreateItem(localFolder, SettingsDirectoryName, StorageItemKind.Folder);
			if (created is WindowsFolder folder)
			{
				return folder;
			}

			throw new IOException("The Windows Shell did not return the settings folder.");
		}
		catch (Exception) when (TryGetSettingsFolder() is { } raceFolder)
		{
			return raceFolder;
		}
	}

	private WindowsStorable CreateItem(WindowsFolder parent, string name, StorageItemKind kind)
	{
		var source = _windowsSource ?? throw new InvalidOperationException("Windows settings storage is not configured.");
		var operations = _windowsOperations ?? throw new InvalidOperationException("Windows settings operations are not configured.");
		var request = new CreateItemOperationRequest(CreateReference(parent), name, kind);
		var result = operations.ExecuteAsync(request, cancellationToken: CancellationToken.None).AsTask().GetAwaiter().GetResult();
		if (!result.Succeeded)
		{
			throw result.Error ?? new IOException($"The Windows Shell could not create '{name}'.");
		}

		if (result.ResultItem is null)
		{
			throw new IOException($"The Windows Shell did not return the created item '{name}'.");
		}

		var item = source.ResolveAsync(result.ResultItem).GetAwaiter().GetResult();
		if (item is not WindowsStorable windowsItem)
		{
			throw new IOException($"The Windows Shell returned an invalid item for '{name}'.");
		}

		return windowsItem;
	}

	private WindowsFile CreateTemporaryFile(WindowsFolder parent, string name)
	{
		var source = _windowsSource ?? throw new InvalidOperationException("Windows settings storage is not configured.");
		var parentPath = parent.FileSystemPath ?? throw new IOException("The settings folder does not have a file-system path.");
		var path = Path.Combine(parentPath, name);
		using var handle = PInvoke.CreateFile(path, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE, null,
			FILE_CREATION_DISPOSITION.CREATE_NEW, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
		var lastError = Marshal.GetLastPInvokeError();
		if (handle.IsInvalid)
		{
			throw new Win32Exception(lastError, $"The Windows Shell could not create '{name}'.");
		}

		var item = source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path)).GetAwaiter().GetResult();
		if (item is not WindowsFile file)
		{
			throw new IOException("The Windows Shell did not return the temporary settings file.");
		}

		return file;
	}

	private WindowsFile? TryGetSettingsFile()
	{
		try
		{
			if (TryGetSettingsFolder() is not { } settingsFolder)
			{
				return null;
			}

			return FindChildByName(settingsFolder, StorableType.File, SettingsFileName) as WindowsFile;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or COMException)
		{
			Debug.WriteLine($"Failed to resolve application settings: {exception}");

			return null;
		}
	}

	private WindowsFolder? TryGetSettingsFolder()
	{
		var localFolder = _localFolder;
		if (localFolder is null)
		{
			return null;
		}

		return FindChildByName(localFolder, StorableType.Folder, SettingsDirectoryName) as WindowsFolder;
	}

	private StorableReference CreateReference(WindowsStorable item)
	{
		var source = _windowsSource ?? throw new InvalidOperationException("Windows settings storage is not configured.");

		return new StorableReference(source.SourceId, item.Id, item.Address);
	}

	private static WindowsFolder ResolveFolder(WindowsStorageSource source, string path)
	{
		var item = source.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path)).GetAwaiter().GetResult();
		if (item is not WindowsFolder folder)
		{
			throw new InvalidOperationException($"The Windows Shell path '{path}' is not a folder.");
		}

		return folder;
	}

	private static WindowsStorable? FindChildByName(WindowsFolder folder, StorableType type, string name)
	{
		return FindChildByNameAsync(folder, type, name).GetAwaiter().GetResult();
	}

	private static async Task<WindowsStorable?> FindChildByNameAsync(WindowsFolder folder, StorableType type, string name)
	{
		await foreach (var child in folder.GetItemsAsync(type, CancellationToken.None).ConfigureAwait(false))
		{
			if (child is WindowsStorable item && StringComparer.OrdinalIgnoreCase.Equals(item.Name, name))
			{
				return item;
			}
		}

		return null;
	}

	private static void WriteText(WindowsFile file, string json)
	{
		using var stream = file.OpenStreamAsync(FileAccess.Write).GetAwaiter().GetResult();
		using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: false);
		writer.Write(json);
		writer.Flush();
	}

	private void CommitTemporaryFile(WindowsFolder settingsFolder, WindowsFile temporaryFile)
	{
		if (FindChildByName(settingsFolder, StorableType.File, SettingsFileName) is WindowsFile destinationFile)
		{
			if (temporaryFile.FileSystemPath is not { } temporaryPath || destinationFile.FileSystemPath is not { } destinationPath)
			{
				throw new IOException("The Windows Shell did not return file-system paths for the settings files.");
			}

			var result = PInvoke.ReplaceFile(destinationPath, temporaryPath, null, REPLACE_FILE_FLAGS.REPLACEFILE_WRITE_THROUGH);
			if (!result)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Shell could not replace the settings file.");
			}

			return;
		}

		var operations = _windowsOperations ?? throw new InvalidOperationException("Windows settings operations are not configured.");
		var request = new RenameOperationRequest(CreateReference(temporaryFile), SettingsFileName);
		var operationResult = operations.ExecuteAsync(request, cancellationToken: CancellationToken.None).AsTask().GetAwaiter().GetResult();
		if (!operationResult.Succeeded)
		{
			throw operationResult.Error ?? new IOException("The Windows Shell could not commit the settings file.");
		}
	}

	private void TryDelete(WindowsFile file)
	{
		try
		{
			var operations = _windowsOperations;
			if (operations is null)
			{
				return;
			}

			var result = operations.ExecuteAsync(new DeleteOperationRequest(CreateReference(file), permanently: true), cancellationToken: CancellationToken.None).AsTask().GetAwaiter().GetResult();
			if (!result.Succeeded)
			{
				Debug.WriteLine($"Failed to delete temporary application settings file: {result.Error}");
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
		{
			Debug.WriteLine($"Failed to delete temporary application settings file: {exception}");
		}
	}

	private static string GetDefaultLocalFolderPath()
	{
		var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localApplicationData))
		{
			throw new InvalidOperationException("The local application data path is unavailable.");
		}

		var packageFamilyName = Package.Current.Id.FamilyName;

		return Path.Combine(localApplicationData, "Packages", packageFamilyName, "LocalState");
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}
}

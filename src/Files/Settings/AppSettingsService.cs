// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.Controls;
using Windows.Globalization;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.Settings;

internal sealed partial class AppSettingsService : INotifyPropertyChanged, IDisposable
{
	private const string SettingsDirectoryName = "Settings";
	private const string SettingsFileName = "settings.json";
	private static readonly TimeSpan _defaultSaveDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _saveRetryDelay = TimeSpan.FromSeconds(5);

	private readonly Lock _syncRoot = new();
	private readonly StorageFolder? _localFolder;
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
		: this(ApplicationData.Current.LocalFolder)
	{
	}

	internal AppSettingsService(StorageFolder localFolder, TimeSpan? saveDelay = null)
	{
		ArgumentNullException.ThrowIfNull(localFolder);

		if (saveDelay is { } delay && delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(saveDelay));
		}

		_localFolder = localFolder;
		_saveDelay = saveDelay ?? _defaultSaveDelay;
		_settings = new AppSettingsData();
		Load();
	}

	internal AppSettingsService(AppSettingsData settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		NormalizeSettings(settings);
		_settings = settings;
		_saveDelay = _defaultSaveDelay;
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
		}

		GC.SuppressFinalize(this);
	}

	private void Load()
	{
		if (_localFolder is null)
		{
			return;
		}

		try
		{
			var settingsFolder = TryGetFolder(_localFolder, SettingsDirectoryName);
			if (settingsFolder is null)
			{
				return;
			}

			var settingsFile = TryGetFile(settingsFolder, SettingsFileName);
			if (settingsFile is null)
			{
				return;
			}

			var json = FileIO.ReadTextAsync(settingsFile, UnicodeEncoding.Utf8).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
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
		if (_localFolder is null)
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
		var localFolder = _localFolder;
		if (!_isDirty || localFolder is null)
		{
			return;
		}

		var settingsFolder = localFolder.CreateFolderAsync(SettingsDirectoryName, CreationCollisionOption.OpenIfExists).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
		var json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettingsData);
		StorageFile? temporaryFile = null;
		var committed = false;
		try
		{
			var temporaryName = $"{SettingsFileName}.{Guid.NewGuid():N}.tmp";
			var createdTemporaryFile = settingsFolder.CreateFileAsync(temporaryName, CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
			temporaryFile = createdTemporaryFile;
			FileIO.WriteTextAsync(createdTemporaryFile, json, UnicodeEncoding.Utf8).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
			createdTemporaryFile.MoveAsync(settingsFolder, SettingsFileName, NameCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
			_isDirty = false;
			committed = true;
		}
		finally
		{
			if (!committed && temporaryFile is not null)
			{
				TryDelete(temporaryFile);
			}
		}
	}

	private static StorageFolder? TryGetFolder(StorageFolder parentFolder, string name)
	{
		var item = parentFolder.TryGetItemAsync(name).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		return item as StorageFolder;
	}

	private static StorageFile? TryGetFile(StorageFolder parentFolder, string name)
	{
		var item = parentFolder.TryGetItemAsync(name).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		return item as StorageFile;
	}

	private static void TryDelete(StorageFile file)
	{
		try
		{
			file.DeleteAsync(StorageDeleteOption.PermanentDelete).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
		{
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.Controls;
using Windows.ApplicationModel;
using Windows.Globalization;

namespace Files.Settings;

internal sealed partial class AppSettingsService : INotifyPropertyChanged, IDisposable
{
	private const string SettingsFileName = "settings.json";
	private static readonly TimeSpan _defaultSaveDelay = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan _saveRetryDelay = TimeSpan.FromSeconds(5);

	private readonly ISettingsStorage? _storage;
	private readonly Lock _syncRoot = new();
	private readonly TimeSpan _saveDelay;
	private AppSettingsData _settings;
	private Timer? _saveTimer;
	private bool _isDirty;
	private bool _isDisposed;

	[GeneratedSettingsProperty("")]
	public partial string LanguageTag { get; set; }

	[GeneratedSettingsProperty(true)]
	public partial bool ShowFileExtensions { get; set; }

	[GeneratedSettingsProperty(false)]
	public partial bool ShowHiddenItems { get; set; }

	[GeneratedSettingsProperty(AppThemeMode.System)]
	public partial AppThemeMode ThemeMode { get; set; }

	[GeneratedSettingsProperty(320d, MinValue = 1d)]
	public partial double PreviewPaneWidth { get; set; }

	[GeneratedSettingsProperty(false)]
	public partial bool IsPreviewPaneVisible { get; set; }

	[GeneratedSettingsProperty(SidebarDisplayMode.Expanded)]
	public partial SidebarDisplayMode SidebarDisplayMode { get; set; }

	internal AppSettingsService(ISettingsStorage storage, TimeSpan? saveDelay = null)
	{
		ArgumentNullException.ThrowIfNull(storage);
		if (saveDelay is { } delay && delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(saveDelay));
		}

		_storage = storage;
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

	public event PropertyChangedEventHandler? PropertyChanged;

	public void ApplyLanguage()
	{
		ApplicationLanguages.PrimaryLanguageOverride = LanguageTag;
	}

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

	internal static string GetDefaultLocalFolderPath()
	{
		var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(localApplicationData))
		{
			throw new InvalidOperationException("The local application data path is unavailable.");
		}

		var packageFamilyName = Package.Current.Id.FamilyName;

		return Path.Combine(localApplicationData, "Packages", packageFamilyName, "LocalState");
	}

	private void Load()
	{
		try
		{
			var json = _storage?.ReadTextAsync(SettingsFileName).GetAwaiter().GetResult();
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
		if (_storage is null)
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
		if (!_isDirty || _storage is null)
		{
			return;
		}

		var json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettingsData);
		_storage.WriteTextAsync(SettingsFileName, json).GetAwaiter().GetResult();
		_isDirty = false;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}
}

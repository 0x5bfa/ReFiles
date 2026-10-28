// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Files.Core.Browsing;
using Files.Core.Models;
using Files.Core.Storage;
using Files.Core.ViewSettings;
using Files.Core.Storage.Windows;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Files.Settings;

internal sealed partial class JsonViewSettingsStore : IViewSettingsStore, IDisposable
{
	private const string SettingsDirectoryName = "Settings";
	private const string SettingsFileName = "view-state.json";
	private const int DefaultSaveDelayMilliseconds = 250;
	private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(5);

	private readonly Lock _syncRoot = new();
	private readonly Lock _initializationLock = new();
	private readonly WindowsStorageSource _windowsSource;
	private readonly WindowsStorageOperationHandler _windowsOperations;
	private readonly string _localFolderPath;
	private readonly TimeSpan _saveDelay;
	private readonly SemaphoreSlim _saveGate = new(1, 1);
	private readonly Dictionary<string, BrowseViewSettings> _values = new(StringComparer.Ordinal);
	private WindowsFolder? _localFolder;
	private WindowsFolder? _settingsFolder;
	private Task? _initializationTask;
	private Timer? _saveTimer;
	private long _changeVersion;
	private bool _isDirty;
	private bool _isInitialized;
	private bool _isDisposed;

	internal JsonViewSettingsStore(WindowsStorageSource source, string localFolderPath, TimeSpan? saveDelay = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);

		if (saveDelay is { } delay && delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(saveDelay));
		}

		_windowsSource = source;
		_windowsOperations = new WindowsStorageOperationHandler(source);
		_localFolderPath = Path.GetFullPath(localFolderPath);
		_saveDelay = saveDelay ?? TimeSpan.FromMilliseconds(DefaultSaveDelayMilliseconds);
	}

	internal async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
	{
		Task initializationTask;
		lock (_initializationLock)
		{
			ThrowIfDisposed();
			if (_isInitialized)
			{
				return;
			}

			_initializationTask ??= LoadAsync();
			initializationTask = _initializationTask;
		}

		await initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		cancellationToken.ThrowIfCancellationRequested();

		if (!ViewSettingsKey.TryForLocation(location, out var key))
		{
			return null;
		}

		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		lock (_syncRoot)
		{
			ThrowIfDisposed();

			return _values.GetValueOrDefault(key.Value);
		}
	}

	public async ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);
		ArgumentNullException.ThrowIfNull(settings);
		cancellationToken.ThrowIfCancellationRequested();

		if (!ViewSettingsKey.TryForLocation(location, out var key))
		{
			return;
		}

		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		lock (_syncRoot)
		{
			ThrowIfDisposed();
			_values[key.Value] = settings;
			_changeVersion++;
			_isDirty = true;
			QueueSave_NoLock();
		}
	}

	internal void SaveNow()
	{
		ThrowIfDisposed();
		InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
		SavePendingAsync().GetAwaiter().GetResult();
	}

	public void Dispose()
	{
		lock (_syncRoot)
		{
			if (_isDisposed)
			{
				return;
			}
		}

		try
		{
			SaveNow();
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"Failed to save view settings: {exception}");
		}

		lock (_syncRoot)
		{
			_isDisposed = true;
			_saveTimer?.Dispose();
			_saveTimer = null;
		}

		_saveGate.Dispose();
		GC.SuppressFinalize(this);
	}

	private async Task LoadAsync()
	{
		var localFolder = await ResolveFolderAsync(_localFolderPath, CancellationToken.None).ConfigureAwait(false);
		var settingsFolder = await FindChildByNameAsync(localFolder, StorableType.Folder, SettingsDirectoryName, CancellationToken.None).ConfigureAwait(false) as WindowsFolder;
		var values = new Dictionary<string, BrowseViewSettings>(StringComparer.Ordinal);
		if (settingsFolder is not null && await FindChildByNameAsync(settingsFolder, StorableType.File, SettingsFileName, CancellationToken.None).ConfigureAwait(false) is WindowsFile settingsFile)
		{
			try
			{
				using var stream = await settingsFile.OpenReadAsync(CancellationToken.None).ConfigureAwait(false);
				using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: true);
				var json = await reader.ReadToEndAsync().ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(json))
				{
					var document = JsonSerializer.Deserialize(json, ViewSettingsJsonContext.Default.ViewSettingsDocument);
					if (document?.SchemaVersion == 1)
					{
						foreach (var entry in document.Entries ?? [])
						{
							if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value?.TryCreateSettings() is not { } settings)
							{
								continue;
							}

							values[entry.Key] = settings;
						}
					}
				}
			}
			catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or COMException)
			{
				Debug.WriteLine($"Failed to load view settings: {exception}");
			}
		}

		lock (_syncRoot)
		{
			_localFolder = localFolder;
			_settingsFolder = settingsFolder;
			_values.Clear();
			foreach (var entry in values)
			{
				_values[entry.Key] = entry.Value;
			}

			_isInitialized = true;
		}
	}

	private void QueueSave_NoLock(TimeSpan? delay = null)
	{
		var saveTimer = _saveTimer ??= new Timer(static state => ((JsonViewSettingsStore)state!).SaveTimerElapsed(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		saveTimer.Change(delay ?? _saveDelay, Timeout.InfiniteTimeSpan);
	}

	private void SaveTimerElapsed()
	{
		_ = SavePendingAsync();
	}

	private async Task SavePendingAsync()
	{
		try
		{
			await _saveGate.WaitAsync().ConfigureAwait(false);
			try
			{
				while (true)
				{
					Dictionary<string, BrowseViewSettings> snapshot;
					long version;
					lock (_syncRoot)
					{
						if (_isDisposed || !_isDirty)
						{
							return;
						}

						snapshot = new Dictionary<string, BrowseViewSettings>(_values, StringComparer.Ordinal);
						version = _changeVersion;
					}

					await PersistAsync(snapshot).ConfigureAwait(false);
					lock (_syncRoot)
					{
						if (_changeVersion == version)
						{
							_isDirty = false;
							return;
						}
					}
				}
			}
			finally
			{
				_saveGate.Release();
			}
		}
		catch (Exception exception)
		{
			lock (_syncRoot)
			{
				if (!_isDisposed && _isDirty)
				{
					QueueSave_NoLock(SaveRetryDelay);
				}
			}

			Debug.WriteLine($"Failed to save view settings: {exception}");
		}
	}

	private async Task PersistAsync(IReadOnlyDictionary<string, BrowseViewSettings> values)
	{
		var settingsFolder = await EnsureSettingsFolderAsync(CancellationToken.None).ConfigureAwait(false);
		var entries = new Dictionary<string, StoredBrowseViewState?>(StringComparer.Ordinal);
		foreach (var entry in values)
		{
			entries[entry.Key] = StoredBrowseViewState.FromSettings(entry.Value);
		}

		var document = new ViewSettingsDocument
		{
			SchemaVersion = 1,
			Entries = entries,
		};
		var json = JsonSerializer.Serialize(document, ViewSettingsJsonContext.Default.ViewSettingsDocument);
		var temporaryFile = await CreateTemporaryFileAsync(settingsFolder, $"{SettingsFileName}.{Guid.NewGuid():N}.tmp", CancellationToken.None).ConfigureAwait(false);
		var committed = false;
		try
		{
			await WriteTextAsync(temporaryFile, json, CancellationToken.None).ConfigureAwait(false);
			await CommitTemporaryFileAsync(settingsFolder, temporaryFile, CancellationToken.None).ConfigureAwait(false);
			committed = true;
		}
		finally
		{
			if (!committed)
			{
				await TryDeleteAsync(temporaryFile).ConfigureAwait(false);
			}
		}
	}

	private async Task<WindowsFolder> EnsureSettingsFolderAsync(CancellationToken cancellationToken)
	{
		WindowsFolder? settingsFolder;
		lock (_syncRoot)
		{
			settingsFolder = _settingsFolder;
		}

		if (settingsFolder is not null)
		{
			return settingsFolder;
		}

		var localFolder = _localFolder ?? throw new InvalidOperationException("The Windows local folder is not initialized.");
		settingsFolder = await FindChildByNameAsync(localFolder, StorableType.Folder, SettingsDirectoryName, cancellationToken).ConfigureAwait(false) as WindowsFolder;
		if (settingsFolder is null)
		{
			var result = await _windowsOperations.ExecuteAsync(new CreateItemOperationRequest(CreateReference(localFolder), SettingsDirectoryName, StorageItemKind.Folder), cancellationToken: cancellationToken).ConfigureAwait(false);
			if (!result.Succeeded || result.ResultItem is null)
			{
				throw result.Error ?? new IOException("The Windows Shell could not create the view settings folder.");
			}

			if (await _windowsSource.ResolveAsync(result.ResultItem, cancellationToken).ConfigureAwait(false) is not WindowsFolder createdFolder)
			{
				throw new IOException("The Windows Shell did not return the view settings folder.");
			}

			settingsFolder = createdFolder;
		}

		lock (_syncRoot)
		{
			_settingsFolder ??= settingsFolder;

			return _settingsFolder;
		}
	}

	private async Task<WindowsFile> CreateTemporaryFileAsync(WindowsFolder parent, string name, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var parentPath = parent.FileSystemPath ?? throw new IOException("The view settings folder does not have a file-system path.");
		var path = Path.Combine(parentPath, name);
		using var handle = PInvoke.CreateFile(path, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE,
			FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE, null,
			FILE_CREATION_DISPOSITION.CREATE_NEW, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);
		var lastError = Marshal.GetLastPInvokeError();
		if (handle.IsInvalid)
		{
			throw new Win32Exception(lastError, $"The Windows Shell could not create '{name}'.");
		}

		return await ResolveFileAsync(path, cancellationToken).ConfigureAwait(false);
	}

	private async Task CommitTemporaryFileAsync(WindowsFolder settingsFolder, WindowsFile temporaryFile, CancellationToken cancellationToken)
	{
		if (await FindChildByNameAsync(settingsFolder, StorableType.File, SettingsFileName, cancellationToken).ConfigureAwait(false) is WindowsFile destinationFile)
		{
			if (temporaryFile.FileSystemPath is not { } temporaryPath || destinationFile.FileSystemPath is not { } destinationPath)
			{
				throw new IOException("The Windows Shell did not return file-system paths for the view settings files.");
			}

			if (!PInvoke.ReplaceFile(destinationPath, temporaryPath, null, REPLACE_FILE_FLAGS.REPLACEFILE_WRITE_THROUGH))
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows Shell could not replace the view settings file.");
			}

			return;
		}

		var result = await _windowsOperations.ExecuteAsync(new RenameOperationRequest(CreateReference(temporaryFile), SettingsFileName), cancellationToken: cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			throw result.Error ?? new IOException("The Windows Shell could not commit the view settings file.");
		}
	}

	private async Task TryDeleteAsync(WindowsFile file)
	{
		try
		{
			var result = await _windowsOperations.ExecuteAsync(new DeleteOperationRequest(CreateReference(file), permanently: true), cancellationToken: CancellationToken.None).ConfigureAwait(false);
			if (!result.Succeeded)
			{
				Debug.WriteLine($"Failed to delete temporary view settings file: {result.Error}");
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
		{
			Debug.WriteLine($"Failed to delete temporary view settings file: {exception}");
		}
	}

	private async Task<WindowsFolder> ResolveFolderAsync(string path, CancellationToken cancellationToken)
	{
		if (await _windowsSource.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path), cancellationToken).ConfigureAwait(false) is not WindowsFolder folder)
		{
			throw new InvalidOperationException($"The Windows Shell path '{path}' is not a folder.");
		}

		return folder;
	}

	private async Task<WindowsFile> ResolveFileAsync(string path, CancellationToken cancellationToken)
	{
		if (await _windowsSource.ResolveAsync(new StorageAddress(WindowsStorageSource.FileAddressScheme, path), cancellationToken).ConfigureAwait(false) is not WindowsFile file)
		{
			throw new IOException("The Windows Shell did not return the temporary view settings file.");
		}

		return file;
	}

	private static async Task<WindowsStorable?> FindChildByNameAsync(WindowsFolder folder, StorableType type, string name, CancellationToken cancellationToken)
	{
		await foreach (var child in folder.GetItemsAsync(type, cancellationToken).ConfigureAwait(false))
		{
			if (child is WindowsStorable item && StringComparer.OrdinalIgnoreCase.Equals(item.Name, name))
			{
				return item;
			}
		}

		return null;
	}

	private StorableReference CreateReference(WindowsStorable item)
	{
		return new StorableReference(_windowsSource.SourceId, item.Id, item.Address);
	}

	private static async Task WriteTextAsync(WindowsFile file, string json, CancellationToken cancellationToken)
	{
		await using var stream = await file.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
		await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: false);
		await writer.WriteAsync(json).ConfigureAwait(false);
		await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.Core.Browsing;
using Files.Core.ViewSettings;

namespace Files.Settings;

internal sealed partial class JsonViewSettingsStore : IViewSettingsStore, IDisposable
{
	private const string SettingsFileName = "view-state.json";
	private const int DefaultSaveDelayMilliseconds = 250;
	private static readonly TimeSpan _saveRetryDelay = TimeSpan.FromSeconds(5);

	private readonly ISettingsStorage _storage;
	private readonly Lock _syncRoot = new();
	private readonly Lock _initializationLock = new();
	private readonly TimeSpan _saveDelay;
	private readonly SemaphoreSlim _saveGate = new(1, 1);
	private readonly Dictionary<string, BrowseViewSettings> _values = new(StringComparer.Ordinal);
	private Task? _initializationTask;
	private Timer? _saveTimer;
	private long _changeVersion;
	private bool _isDirty;
	private bool _isInitialized;
	private bool _isDisposed;

	internal JsonViewSettingsStore(ISettingsStorage storage, TimeSpan? saveDelay = null)
	{
		ArgumentNullException.ThrowIfNull(storage);
		if (saveDelay is { } delay && delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(saveDelay));
		}

		_storage = storage;
		_saveDelay = saveDelay ?? TimeSpan.FromMilliseconds(DefaultSaveDelayMilliseconds);
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

	internal void SaveNow()
	{
		ThrowIfDisposed();
		InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
		SavePendingAsync().GetAwaiter().GetResult();
	}

	private async Task LoadAsync()
	{
		var values = new Dictionary<string, BrowseViewSettings>(StringComparer.Ordinal);
		try
		{
			var json = await _storage.ReadTextAsync(SettingsFileName).ConfigureAwait(false);
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

		lock (_syncRoot)
		{
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
					QueueSave_NoLock(_saveRetryDelay);
				}
			}

			Debug.WriteLine($"Failed to save view settings: {exception}");
		}
	}

	private async Task PersistAsync(IReadOnlyDictionary<string, BrowseViewSettings> values)
	{
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
		await _storage.WriteTextAsync(SettingsFileName, json).ConfigureAwait(false);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
	}
}

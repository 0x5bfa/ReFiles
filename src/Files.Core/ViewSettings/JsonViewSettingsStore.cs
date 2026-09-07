// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Files.Core.Browsing;

namespace Files.Core.ViewSettings;

/// <summary>Persists view settings overrides in a versioned JSON document.</summary>
public sealed partial class JsonViewSettingsStore : IViewSettingsStore
{
	/// <summary>Gets the schema version written by this store.</summary>
	public const int CurrentSchemaVersion = 1;

	private readonly string _filePath;
	private readonly string _lockFilePath;
	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <summary>Initializes a JSON view settings store.</summary>
	/// <param name="filePath">The path of the JSON settings file.</param>
	public JsonViewSettingsStore(string filePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		_filePath = Path.GetFullPath(filePath);
		_lockFilePath = $"{_filePath}.lock";
	}

	/// <summary>Gets the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored override, or <see langword="null"/> when none exists.</returns>
	public async ValueTask<BrowseViewSettingsOverride?> GetAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var processLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
			var values = await LoadValuesWithRecoveryAsync(cancellationToken).ConfigureAwait(false);

			return values.GetValueOrDefault(scope);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Gets complete settings stored for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored settings applied to defaults, or <see langword="null"/> when none exists.</returns>
	public async ValueTask<BrowseViewSettings?> GetAsync(BrowseLocation location, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		var settingsOverride = await GetAsync(ViewSettingsScopeKey.ForLocation(location), cancellationToken).ConfigureAwait(false);
		if (settingsOverride is null)
		{
			return null;
		}

		return settingsOverride.Fields == ViewSettingsOverrideFields.All ? settingsOverride.Values : settingsOverride.ApplyTo(BrowseViewSettings.Default);
	}

	/// <summary>Stores a settings override for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="settingsOverride">The settings override to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public async ValueTask SetAsync(ViewSettingsScopeKey scope, BrowseViewSettingsOverride settingsOverride, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		ArgumentNullException.ThrowIfNull(settingsOverride);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var processLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
			var updatedValues = await LoadValuesWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
			updatedValues = new Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>(updatedValues)
			{
				[scope] = settingsOverride,
			};

			await PersistAsync(updatedValues, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Atomically replaces selected fields in the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="fields">The fields to replace or clear.</param>
	/// <param name="replacement">Replacement values whose supplied fields must be a subset of <paramref name="fields"/>.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The stored override after the patch, or <see langword="null"/> when no fields remain.</returns>
	public async ValueTask<BrowseViewSettingsOverride?> PatchAsync(ViewSettingsScopeKey scope, ViewSettingsOverrideFields fields, BrowseViewSettingsOverride replacement,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		ArgumentNullException.ThrowIfNull(replacement);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var processLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
			var values = await LoadValuesWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
			var current = values.GetValueOrDefault(scope) ?? new BrowseViewSettingsOverride(ViewSettingsOverrideFields.None, BrowseViewSettings.Default);
			var updated = current.ReplaceFields(fields, replacement);
			if (updated.Fields == ViewSettingsOverrideFields.None && !values.ContainsKey(scope))
			{
				return null;
			}

			var updatedValues = new Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>(values);
			if (updated.Fields == ViewSettingsOverrideFields.None)
			{
				updatedValues.Remove(scope);
			}
			else
			{
				updatedValues[scope] = updated;
			}

			await PersistAsync(updatedValues, cancellationToken).ConfigureAwait(false);

			return updated.Fields == ViewSettingsOverrideFields.None ? null : updated;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Stores complete settings for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="settings">The complete settings to store.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	public ValueTask SetAsync(BrowseLocation location, BrowseViewSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(location);

		ArgumentNullException.ThrowIfNull(settings);

		return SetAsync(ViewSettingsScopeKey.ForLocation(location), BrowseViewSettingsOverride.FromSettings(settings), cancellationToken);
	}

	/// <summary>Removes the settings override stored for a view scope.</summary>
	/// <param name="scope">The stable view scope.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns><see langword="true"/> when an override was removed.</returns>
	public async ValueTask<bool> RemoveAsync(ViewSettingsScopeKey scope, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scope);

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await using var processLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
			var values = await LoadValuesWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
			if (!values.ContainsKey(scope))
			{
				return false;
			}

			var updatedValues = new Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>(values);
			updatedValues.Remove(scope);
			await PersistAsync(updatedValues, cancellationToken).ConfigureAwait(false);

			return true;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(GetDirectoryPath());
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.Asynchronous);
			}
			catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private async ValueTask<Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>> LoadValuesWithRecoveryAsync(CancellationToken cancellationToken)
	{
		if (!File.Exists(_filePath))
		{
			return [];
		}

		try
		{
			return await LoadValuesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (InvalidDataException)
		{
			QuarantineInvalidFile();

			return [];
		}
	}

	private async ValueTask<Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>> LoadValuesAsync(CancellationToken cancellationToken)
	{
		ViewSettingsDocument? document;
		try
		{
			await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
			document = await JsonSerializer.DeserializeAsync(stream, ViewSettingsJsonSerializerContext.Default.ViewSettingsDocument, cancellationToken).ConfigureAwait(false);
		}
		catch (JsonException exception)
		{
			throw new InvalidDataException("The view settings file does not contain valid JSON.", exception);
		}

		if (document is null)
		{
			throw new InvalidDataException("The view settings file is empty.");
		}

		if (document.SchemaVersion != CurrentSchemaVersion)
		{
			throw new InvalidDataException($"View settings schema version {document.SchemaVersion} is not supported.");
		}

		if (document.Entries is null)
		{
			throw new InvalidDataException("The view settings file does not contain an entries object.");
		}

		var loadedValues = new Dictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride>();
		foreach (var entry in document.Entries)
		{
			try
			{
				loadedValues.Add(new ViewSettingsScopeKey(entry.Key), FromDocument(entry.Value));
			}
			catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
			{
				throw new InvalidDataException($"The view settings entry '{entry.Key}' is invalid.", exception);
			}
		}

		return loadedValues;
	}

	private void QuarantineInvalidFile()
	{
		var directoryPath = GetDirectoryPath();
		var fileName = Path.GetFileNameWithoutExtension(_filePath);
		var extension = Path.GetExtension(_filePath);
		var invalidPath = Path.Combine(directoryPath, $"{fileName}.invalid-{Guid.NewGuid():N}{extension}");
		File.Move(_filePath, invalidPath);
	}

	private async ValueTask PersistAsync(IReadOnlyDictionary<ViewSettingsScopeKey, BrowseViewSettingsOverride> values, CancellationToken cancellationToken)
	{
		var directoryPath = GetDirectoryPath();
		Directory.CreateDirectory(directoryPath);
		var tempPath = Path.Combine(directoryPath, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
		var document = new ViewSettingsDocument
		{
			SchemaVersion = CurrentSchemaVersion,
			Entries = values.ToDictionary(static entry => entry.Key.Value, static entry => ToDocument(entry.Value), StringComparer.Ordinal),
		};

		try
		{
			await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
			{
				await JsonSerializer.SerializeAsync(stream, document, ViewSettingsJsonSerializerContext.Default.ViewSettingsDocument, cancellationToken).ConfigureAwait(false);
				await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
				stream.Flush(flushToDisk: true);
			}

			if (File.Exists(_filePath))
			{
				File.Replace(tempPath, _filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
			}
			else
			{
				File.Move(tempPath, _filePath);
			}
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
	}

	private string GetDirectoryPath()
	{
		return Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("The view settings file path does not have a parent directory.");
	}

	private static BrowseViewSettingsOverride FromDocument(ViewSettingsOverrideDocument? document)
	{
		if (document?.Values is null)
		{
			throw new InvalidDataException("The view settings override does not contain values.");
		}

		if (document.Values.DetailsColumns is null)
		{
			throw new InvalidDataException("The view settings override does not contain Details view columns.");
		}

		var values = new BrowseViewSettings(
			(ViewLayoutMode)document.Values.LayoutMode,
			document.Values.DetailsColumns.Select(FromDocument),
			document.Values.SortPropertyId,
			(ViewSortDirection)document.Values.SortDirection,
			document.Values.ItemSize,
			document.Values.GroupPropertyId,
			(ViewSortDirection)document.Values.GroupDirection);

		return new BrowseViewSettingsOverride((ViewSettingsOverrideFields)document.Fields, values, (ViewColumnSettingsMode)document.ColumnMode);
	}

	private static ViewColumnSettings FromDocument(ViewColumnDocument? document)
	{
		if (document?.PropertyId is null)
		{
			throw new InvalidDataException("A view settings column does not contain a property ID.");
		}

		return new ViewColumnSettings(document.PropertyId, document.Width, document.Order, document.IsVisible);
	}

	private static ViewSettingsOverrideDocument ToDocument(BrowseViewSettingsOverride settingsOverride)
	{
		return new ViewSettingsOverrideDocument
		{
			Fields = (int)settingsOverride.Fields,
			ColumnMode = (int)settingsOverride.ColumnMode,
			Values = new BrowseViewSettingsDocument
			{
				LayoutMode = (int)settingsOverride.Values.LayoutMode,
				DetailsColumns = settingsOverride.Values.Columns.Select(ToDocument).ToList(),
				SortPropertyId = settingsOverride.Values.SortPropertyId,
				SortDirection = (int)settingsOverride.Values.SortDirection,
				ItemSize = settingsOverride.Values.ItemSize,
				GroupPropertyId = settingsOverride.Values.GroupPropertyId,
				GroupDirection = (int)settingsOverride.Values.GroupDirection,
			},
		};
	}

	private static ViewColumnDocument ToDocument(ViewColumnSettings column)
	{
		return new ViewColumnDocument
		{
			PropertyId = column.PropertyId,
			Width = column.Width,
			Order = column.Order,
			IsVisible = column.IsVisible,
		};
	}

	private sealed class ViewSettingsDocument
	{
		public int SchemaVersion { get; set; }

		public Dictionary<string, ViewSettingsOverrideDocument?>? Entries { get; set; }
	}

	private sealed class ViewSettingsOverrideDocument
	{
		public int Fields { get; set; }

		public int ColumnMode { get; set; }

		public BrowseViewSettingsDocument? Values { get; set; }
	}

	private sealed class BrowseViewSettingsDocument
	{
		public int LayoutMode { get; set; }

		public List<ViewColumnDocument?>? DetailsColumns { get; set; }

		public string? SortPropertyId { get; set; }

		public int SortDirection { get; set; }

		public double? ItemSize { get; set; }

		public string? GroupPropertyId { get; set; }

		public int GroupDirection { get; set; }
	}

	private sealed class ViewColumnDocument
	{
		public string? PropertyId { get; set; }

		public double Width { get; set; }

		public int Order { get; set; }

		public bool IsVisible { get; set; }
	}

	[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
	[JsonSerializable(typeof(ViewSettingsDocument))]
	private sealed partial class ViewSettingsJsonSerializerContext : JsonSerializerContext
	{
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Files.Core.Browsing;
using Files.Core.Storage;

namespace Files.Core.ViewSettings;

/// <summary>
/// Identifies the stable persistence scope for one browse location.
/// </summary>
public sealed record ViewSettingsKey
{
	/// <summary>Gets the serialized key value.</summary>
	public string Value { get; }

	/// <summary>Initializes a view settings key.</summary>
	/// <param name="value">The serialized key value.</param>
	public ViewSettingsKey(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Value = value;
	}

	/// <summary>Creates a stable key for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <returns>The stable view settings key.</returns>
	/// <exception cref="NotSupportedException">The location type is not supported.</exception>
	public static ViewSettingsKey ForLocation(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		if (TryForLocation(location, out var key))
		{
			return key;
		}

		throw new NotSupportedException($"Browse location type '{location.GetType().FullName}' does not define a view settings key.");
	}

	/// <summary>Tries to create a stable key for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="key">The stable key when the location is supported.</param>
	/// <returns><see langword="true"/> when a stable key was created.</returns>
	public static bool TryForLocation(BrowseLocation location, [NotNullWhen(true)] out ViewSettingsKey? key)
	{
		ArgumentNullException.ThrowIfNull(location);

		key = location switch
		{
			FolderLocation folder => new ViewSettingsKey($"v1/folder/{EncodeReference(folder.Folder)}"),
			ArchiveLocation archive => new ViewSettingsKey($"v1/archive/{EncodeReference(archive.Archive)}/{EncodeComponent(archive.EntryPath)}"),
			HomeLocation => new ViewSettingsKey("v1/home"),
			SearchLocation { Scope: null } => new ViewSettingsKey("v1/search/global"),
			SearchLocation search => new ViewSettingsKey($"v1/search/{EncodeReference(search.Scope!)}"),
			TagLocation tag => new ViewSettingsKey($"v1/tag/{EncodeComponent(tag.TagId)}"),
			_ => null,
		};

		return key is not null;
	}

	/// <summary>Returns the serialized key value.</summary>
	/// <returns>The serialized key value.</returns>
	public override string ToString() => Value;

	private static string EncodeReference(StorableReference reference)
	{
		return $"{EncodeComponent(reference.SourceId.Value)}/{EncodeComponent(reference.ItemId)}";
	}

	private static string EncodeComponent(string value)
	{
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}

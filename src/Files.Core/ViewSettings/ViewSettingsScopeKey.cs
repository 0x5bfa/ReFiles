// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Files.Core.Browsing;
using Files.Core.Storage;

namespace Files.Core.ViewSettings;

/// <summary>Identifies a stable scope for persisted browse view settings.</summary>
public sealed record ViewSettingsScopeKey
{
	/// <summary>Gets the serialized scope key.</summary>
	public string Value { get; }

	/// <summary>Initializes a view settings scope key.</summary>
	/// <param name="value">The serialized scope key.</param>
	public ViewSettingsScopeKey(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Value = value;
	}

	/// <summary>Creates the stable view settings scope for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <returns>The stable view settings scope.</returns>
	/// <exception cref="NotSupportedException">The browse location type is not supported.</exception>
	public static ViewSettingsScopeKey ForLocation(BrowseLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		if (TryForLocation(location, out var scope))
		{
			return scope;
		}

		throw new NotSupportedException($"Browse location type '{location.GetType().FullName}' does not define a view settings scope.");
	}

	/// <summary>Tries to create the stable view settings scope for a browse location.</summary>
	/// <param name="location">The browse location.</param>
	/// <param name="scope">The stable scope when the location supports persistent view settings.</param>
	/// <returns><see langword="true"/> when a stable scope is available.</returns>
	public static bool TryForLocation(BrowseLocation location, [NotNullWhen(true)] out ViewSettingsScopeKey? scope)
	{
		ArgumentNullException.ThrowIfNull(location);

		scope = location switch
		{
			FolderLocation folder => new ViewSettingsScopeKey($"v1/folder/{EncodeReference(folder.Folder)}"),
			ArchiveLocation archive => new ViewSettingsScopeKey($"v1/archive/{EncodeReference(archive.Archive)}/{EncodeComponent(archive.EntryPath)}"),
			HomeLocation => new ViewSettingsScopeKey("v1/home"),
			SearchLocation { Scope: null } => new ViewSettingsScopeKey("v1/search/global"),
			SearchLocation search => new ViewSettingsScopeKey($"v1/search/{EncodeReference(search.Scope!)}"),
			TagLocation tag => new ViewSettingsScopeKey($"v1/tag/{EncodeComponent(tag.TagId)}"),
			IViewSettingsScopeProvider provider => provider.ViewSettingsScope ?? throw new InvalidOperationException("The custom view settings scope provider returned null."),
			_ => null,
		};

		return scope is not null;
	}

	/// <summary>Returns the serialized scope key.</summary>
	/// <returns>The serialized scope key.</returns>
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

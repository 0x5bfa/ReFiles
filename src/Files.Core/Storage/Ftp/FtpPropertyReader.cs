// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;
using Files.Core.ItemFeatures;
using Files.Core.ItemFeatures.Properties;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Publishes properties already captured by FTP listings.
/// </summary>
public sealed class FtpPropertyReader : IPropertyReader
{
	private const string ItemTypeText = "System.ItemTypeText";
	private const string Size = "System.Size";
	private const string DateModified = "System.DateModified";
	private const string DateCreated = "System.DateCreated";
	private readonly FtpStorageSource _source;

	public FtpPropertyReader(FtpStorageSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		_source = source;
	}

	public bool CanRead(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return ReferenceEquals(context.Source, _source)
			&& context.CoreModel is FtpStorable;
	}

	public ValueTask<IReadOnlyDictionary<StorableReference, IReadOnlyDictionary<string, object?>>> GetPropertiesAsync(PropertyRequest request, IReadOnlyList<ItemContext> contexts, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(contexts);

		var results =
			new Dictionary<StorableReference, IReadOnlyDictionary<string, object?>>();
		foreach (var context in contexts.Where(CanRead))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var item = (FtpStorable)context.CoreModel;
			var properties =
				new Dictionary<string, object?>(StringComparer.Ordinal);

			foreach (var propertyId in request.PropertyIds)
			{
				switch (propertyId)
				{
					case ItemTypeText:
						properties[propertyId] = item.Kind switch
						{
							FtpEntryKind.Folder => "FTP folder",
							FtpEntryKind.SymbolicLink => "FTP symbolic link",
							_ => "FTP file",
						};
						break;
					case Size when item.Snapshot.Size is { } size:
						properties[propertyId] = (ulong)size;
						break;
					case DateModified
						when item.Snapshot.DateModified is { } modified:
						properties[propertyId] = modified;
						break;
					case DateCreated
						when item.Snapshot.DateCreated is { } created:
						properties[propertyId] = created;
						break;
				}
			}

			results[context.Reference] =
				new ReadOnlyDictionary<string, object?>(properties);
		}

		return ValueTask.FromResult<
			IReadOnlyDictionary<
				StorableReference,
				IReadOnlyDictionary<string, object?>>>(new ReadOnlyDictionary< StorableReference, IReadOnlyDictionary<string, object?>>(results));
	}
}

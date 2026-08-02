// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Ftp;

/// <summary>
/// Represents an immutable snapshot of one FTP item.
/// </summary>
public abstract class FtpStorable :
	IStorableChild,
	IStorageAddressSource,
	IEquatable<FtpStorable>
{
	private readonly FtpStorageSource _source;

	internal FtpStorableFactory Factory { get; }

	internal FtpStorableSnapshot Snapshot { get; }

	public string Id { get; }

	public string Name { get; }

	public StorageAddress Address { get; }

	public FtpPath Path => Snapshot.Path;

	public FtpEntryKind Kind => Snapshot.Kind;

	internal FtpStorable(FtpStorageSource source, FtpStorableSnapshot snapshot, FtpStorableFactory factory)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(factory);

		_source = source;
		Snapshot = snapshot;
		Factory = factory;
		Id = snapshot.Path.Value;
		Name = snapshot.Name;
		Address = source.CreateAddress(snapshot.Path);
	}

	public async Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		var parentPath = Path.Parent;
		if (parentPath is null || !parentPath.IsWithin(_source.Profile.RootPath, _source.Profile.PathComparer))
		{
			return null;
		}

		var parent = await Factory.ResolveAsync(parentPath, cancellationToken).ConfigureAwait(false);

		return parent as IFolder
			?? throw new InvalidOperationException("The FTP parent path did not resolve to a folder.");
	}

	public bool Equals(FtpStorable? other)
	{
		return other is not null
			&& _source.SourceId == other._source.SourceId
			&& StringComparer.Ordinal.Equals(Id, other.Id);
	}

	public override bool Equals(object? obj)
		=> Equals(obj as FtpStorable);

	public override int GetHashCode()
		=> HashCode.Combine(_source.SourceId, StringComparer.Ordinal.GetHashCode(Id));

	public override string ToString() => Address.ToString();
}

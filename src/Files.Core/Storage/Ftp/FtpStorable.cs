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

	/// <summary>Gets the source-specific item identifier.</summary>
	public string Id { get; }

	/// <summary>Gets the item name.</summary>
	public string Name { get; }

	/// <summary>Gets the resolvable storage address.</summary>
	public StorageAddress Address { get; }

	/// <summary>Gets the normalized FTP path.</summary>
	public FtpPath Path => Snapshot.Path;

	/// <summary>Gets the entry kind.</summary>
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

	/// <summary>Gets the parent folder, when one exists.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The parent folder, or <see langword="null"/> at the source root.</returns>
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

	/// <summary>Determines whether another FTP item has the same identity.</summary>
	/// <param name="other">The item to compare.</param>
	/// <returns><see langword="true"/> when both items have the same source and path.</returns>
	public bool Equals(FtpStorable? other)
	{
		return other is not null
			&& _source.SourceId == other._source.SourceId
			&& StringComparer.Ordinal.Equals(Id, other.Id);
	}

	/// <summary>Determines whether another object has the same FTP identity.</summary>
	/// <param name="obj">The object to compare.</param>
	/// <returns><see langword="true"/> when the object is the same FTP item.</returns>
	public override bool Equals(object? obj)
		=> Equals(obj as FtpStorable);

	/// <summary>Gets a hash code based on source and path.</summary>
	/// <returns>The identity hash code.</returns>
	public override int GetHashCode()
		=> HashCode.Combine(_source.SourceId, StringComparer.Ordinal.GetHashCode(Id));

	/// <summary>Returns the item address.</summary>
	/// <returns>The item address.</returns>
	public override string ToString() => Address.ToString();
}

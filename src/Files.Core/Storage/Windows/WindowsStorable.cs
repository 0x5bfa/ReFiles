// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Represents an apartment-neutral snapshot of a Windows Shell item.
/// </summary>
public abstract class WindowsStorable : IWindowsStorable, IEquatable<WindowsStorable>
{
	private readonly WindowsStorableDescriptor _descriptor;

	internal WindowsStorableFactory Factory { get; }

	internal WindowsStorableDescriptor Descriptor => _descriptor;

	internal WindowsStorableSnapshot Snapshot => _descriptor.Snapshot;

	internal WindowsItemLocator Locator => _descriptor.Locator;

	/// <summary>Gets the source-specific item identifier.</summary>
	public string Id { get; }

	/// <summary>Gets the item name.</summary>
	public string Name { get; }

	/// <summary>Gets the storage address.</summary>
	public StorageAddress Address { get; }

	/// <summary>Gets the Windows Shell parsing name.</summary>
	public string ParsingName => _descriptor.Locator.ParsingName;

	/// <summary>Gets the file-system path, when available.</summary>
	public string? FileSystemPath => _descriptor.Snapshot.FileSystemPath;

	/// <summary>Gets a value indicating whether the item is file-system backed.</summary>
	public bool IsFileSystem => FileSystemPath is not null;

	/// <summary>Gets a value indicating whether the item is exposed as a stream.</summary>
	public bool IsStream => _descriptor.Snapshot.IsStream;

	/// <summary>
	/// Gets a value indicating whether the Shell marks the item as hidden.
	/// </summary>
	public bool IsHidden => _descriptor.Snapshot.IsHidden;

	internal WindowsStorable(WindowsStorableDescriptor descriptor, WindowsStorableFactory factory)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(factory);

		_descriptor = descriptor;
		Factory = factory;
		Id = descriptor.ItemId;
		Name = descriptor.Snapshot.Name;
		Address = descriptor.Address;
	}

	/// <summary>Gets the parent folder, when one exists.</summary>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The parent folder, or <see langword="null"/> when none exists.</returns>
	public async Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		return await Factory.GetParentAsync(Descriptor, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Determines whether another Windows item has the same identity.</summary>
	/// <param name="other">The item to compare.</param>
	/// <returns><see langword="true"/> when both items have the same identity.</returns>
	public bool Equals(WindowsStorable? other)
	{
		return other is not null && StringComparer.Ordinal.Equals(Id, other.Id);
	}

	/// <summary>Determines whether another object has the same Windows item identity.</summary>
	/// <param name="obj">The object to compare.</param>
	/// <returns><see langword="true"/> when the object is the same item.</returns>
	public override bool Equals(object? obj) => Equals(obj as WindowsStorable);

	/// <summary>Gets a hash code based on the item identity.</summary>
	/// <returns>The identity hash code.</returns>
	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

	/// <summary>Returns the Shell parsing name.</summary>
	/// <returns>The parsing name.</returns>
	public override string ToString() => ParsingName;
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Windows;

/// <summary>
/// Represents an apartment-neutral snapshot of a Windows Shell item.
/// </summary>
public abstract class WindowsStorable : IWindowsStorable, IEquatable<WindowsStorable>
{
	private readonly WindowsStorableDescriptor descriptor;

	internal WindowsStorable(
		WindowsStorableDescriptor descriptor,
		WindowsStorableFactory factory)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(factory);

		this.descriptor = descriptor;
		Factory = factory;
		Id = descriptor.ItemId;
		Name = descriptor.Snapshot.Name;
		Address = descriptor.Address;
	}

	internal WindowsStorableFactory Factory { get; }

	internal WindowsStorableDescriptor Descriptor => descriptor;

	internal WindowsStorableSnapshot Snapshot => descriptor.Snapshot;

	internal WindowsItemLocator Locator => descriptor.Locator;

	public string Id { get; }

	public string Name { get; }

	public StorageAddress Address { get; }

	public string ParsingName => descriptor.Locator.ParsingName;

	public string? FileSystemPath => descriptor.Snapshot.FileSystemPath;

	public bool IsFileSystem => FileSystemPath is not null;

	public bool IsStream => descriptor.Snapshot.IsStream;

	public async Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
	{
		return await Factory.GetParentAsync(Descriptor, cancellationToken).ConfigureAwait(false);
	}

	public bool Equals(WindowsStorable? other)
	{
		return other is not null && StringComparer.Ordinal.Equals(Id, other.Id);
	}

	public override bool Equals(object? obj) => Equals(obj as WindowsStorable);

	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

	public override string ToString() => ParsingName;
}

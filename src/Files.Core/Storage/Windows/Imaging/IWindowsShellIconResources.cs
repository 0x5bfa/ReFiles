// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Windows;

/// <summary>Loads Windows stock and resource icons independently of storage items.</summary>
public interface IWindowsShellIconResources
{
	/// <summary>Loads the stock elevation shield as PNG data.</summary>
	/// <param name="size">The physical icon size in pixels.</param>
	/// <param name="cancellationToken">The token used to cancel loading.</param>
	/// <returns>The encoded icon, or empty data when unavailable.</returns>
	Task<ReadOnlyMemory<byte>> GetElevationShieldIconAsync(int size = 16, CancellationToken cancellationToken = default);

	/// <summary>Loads the stock closed-folder icon as PNG data.</summary>
	/// <param name="size">The physical icon size in pixels.</param>
	/// <param name="cancellationToken">The token used to cancel loading.</param>
	/// <returns>The encoded icon, or empty data when unavailable.</returns>
	Task<ReadOnlyMemory<byte>> GetFolderIconAsync(int size = 20, CancellationToken cancellationToken = default);

	/// <summary>Loads an icon resource as PNG data without resolving a storage item.</summary>
	/// <param name="path">The icon resource path.</param>
	/// <param name="index">The resource index or negative resource identifier.</param>
	/// <param name="size">The physical icon size in pixels.</param>
	/// <param name="cancellationToken">The token used to cancel loading.</param>
	/// <returns>The encoded icon, or empty data when unavailable.</returns>
	Task<ReadOnlyMemory<byte>> GetResourceIconAsync(string path, int index, int size = 48, CancellationToken cancellationToken = default);
}

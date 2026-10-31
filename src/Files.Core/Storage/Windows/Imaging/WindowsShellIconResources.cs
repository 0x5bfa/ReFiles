// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32.UI.Shell;

namespace Files.Core.Storage.Windows;

/// <summary>Extracts Windows stock and resource icons away from the calling UI thread.</summary>
public sealed class WindowsShellIconResources : IWindowsShellIconResources
{
	/// <inheritdoc/>
	public Task<ReadOnlyMemory<byte>> GetElevationShieldIconAsync(int size = 16, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => WindowsShellIconProvider.GetStockIcon(SHSTOCKICONID.SIID_SHIELD, size, cancellationToken), cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ReadOnlyMemory<byte>> GetFolderIconAsync(int size = 20, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => WindowsShellIconProvider.GetStockIcon(SHSTOCKICONID.SIID_FOLDER, size, cancellationToken), cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ReadOnlyMemory<byte>> GetResourceIconAsync(string path, int index, int size = 48, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

		return Task.Run(() => WindowsShellIconProvider.GetResourceIcon(path, index, size, cancellationToken), cancellationToken);
	}
}

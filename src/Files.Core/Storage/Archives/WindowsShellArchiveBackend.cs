// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Models;
using Files.Core.Storage.Windows;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives;

/// <summary>
/// Reuses a Windows Shell item when the Shell exposes the archive as a folder.
/// </summary>
public sealed class WindowsShellArchiveBackend : IArchiveBackend
{
	public const string DefaultBackendId = "windows-shell-archive";

	public string Id => DefaultBackendId;

	public int Priority => 200;

	public bool SupportsEncryptedArchives => false;

	public async ValueTask<ArchiveMountResult> TryMountAsync(ArchiveMountRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var archiveItem = request.ArchiveModel.GetCoreModel();
		if (request.Source is not WindowsStorageSource || archiveItem is not IWindowsStorable { IsStream: true, } || archiveItem is not IFolder folder)
		{
			return ArchiveMountResult.Unsupported.Instance;
		}

		try
		{
			await using var enumerator = folder.GetItemsAsync(StorableType.All, cancellationToken).GetAsyncEnumerator(cancellationToken);
			_ = await enumerator.MoveNextAsync().ConfigureAwait(false);

			return new ArchiveMountResult.Success(new WindowsShellArchiveMount(request.Archive, request.Source, folder));
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception error)
		{
			return new ArchiveMountResult.Failed(error);
		}
	}
}

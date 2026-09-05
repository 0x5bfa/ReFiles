// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Models;
using Files.Core.Storage.Archives;
using OwlCore.Storage;

namespace Files.Core.Windows;

/// <summary>
/// Reuses a Windows Shell item when the Shell exposes the archive as a folder.
/// </summary>
public sealed class WindowsShellArchiveBackend : IArchiveBackend
{
	/// <summary>Gets the stable backend identifier.</summary>
	public const string DefaultBackendId = "windows-shell-archive";

	/// <summary>Gets the backend identifier.</summary>
	public string Id => DefaultBackendId;

	/// <summary>Gets the backend priority.</summary>
	public int Priority => 200;

	/// <summary>Gets a value indicating whether encrypted archives are supported.</summary>
	public bool SupportsEncryptedArchives => false;

	/// <summary>Attempts to mount an archive exposed as a Windows Shell folder.</summary>
	/// <param name="request">The mount request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The mount result.</returns>
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

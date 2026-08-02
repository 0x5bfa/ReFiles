// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using OwlCore.Storage;

namespace Files.Core.Storage.Archives;

internal static class ArchiveStreamResolver
{
	public static async ValueTask<Stream?> OpenSeekableReadAsync(ArchiveMountRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		Stream? input = null;
		if (request.ArchiveModel.CoreModel is IFile file)
		{
			input = await file.OpenStreamAsync(FileAccess.Read, cancellationToken).ConfigureAwait(false);
		}
		else if (request.ArchiveModel.CoreModel is IStorageAddressSource { Address: { Scheme: var scheme, Value: var path, }, } && scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
		{
			input = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete, Options = FileOptions.Asynchronous | FileOptions.SequentialScan, });
		}

		if (input is null)
		{
			return input;
		}

		if (input.CanSeek)
		{
			input.Position = 0;

			return input;
		}

		FileStream? seekableCopy = null;
		try
		{
			seekableCopy = CreateTemporaryStream();
			await input.CopyToAsync(seekableCopy, cancellationToken).ConfigureAwait(false);
			seekableCopy.Position = 0;
			await input.DisposeAsync().ConfigureAwait(false);

			return seekableCopy;
		}
		catch
		{
			if (seekableCopy is not null)
			{
				await seekableCopy.DisposeAsync().ConfigureAwait(false);
			}

			await input.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	public static FileStream CreateTemporaryStream()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files-{Guid.NewGuid():N}.archive");

		return new FileStream(
			path,
			new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.ReadWrite,
				Share = FileShare.Read | FileShare.Delete,
				Options =
					FileOptions.Asynchronous
					| FileOptions.DeleteOnClose
					| FileOptions.SequentialScan,
			});
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Archives;

internal static class ArchiveStreamResolver
{
	public static async ValueTask<Stream?> OpenSeekableReadAsync(ArchiveMountRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		cancellationToken.ThrowIfCancellationRequested();

		if (request.ArchiveModel.Get<IReadableContent>() is not { } content)
		{
			return null;
		}

		Stream? input = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
		FileStream? seekableCopy = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (input.CanSeek)
			{
				input.Position = 0;
				var result = input;
				input = null;

				return result;
			}

			seekableCopy = CreateTemporaryStream();
			await input.CopyToAsync(seekableCopy, cancellationToken).ConfigureAwait(false);
			seekableCopy.Position = 0;
			var source = input;
			input = null;
			await source.DisposeAsync().ConfigureAwait(false);
			var copy = seekableCopy;
			seekableCopy = null;

			return copy;
		}
		finally
		{
			try
			{
				if (seekableCopy is not null)
				{
					await seekableCopy.DisposeAsync().ConfigureAwait(false);
				}
			}
			finally
			{
				if (input is not null)
				{
					await input.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
	}

	public static FileStream CreateTemporaryStream()
	{
		var path = Path.Combine(Path.GetTempPath(), $"Files-{Guid.NewGuid():N}.archive");

		return new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.Read | FileShare.Delete,
			Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan,
		});
	}
}

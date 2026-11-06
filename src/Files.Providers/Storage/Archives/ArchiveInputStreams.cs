// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities.Content;

namespace Files.Core.Storage.Archives;

internal static class ArchiveInputStreams
{
	public static async Task<Stream> MaterializeAsync(IReadableContent content, CancellationToken cancellationToken)
	{
		FileStream? output = CreateTemporaryStream();
		try
		{
			await using (var input = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();

				await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
			}

			cancellationToken.ThrowIfCancellationRequested();

			output.Position = 0;
			var result = output;
			output = null;

			return result;
		}
		finally
		{
			if (output is not null)
			{
				await output.DisposeAsync().ConfigureAwait(false);
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

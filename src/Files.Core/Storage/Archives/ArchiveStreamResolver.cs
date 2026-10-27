// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.Capabilities;
using Files.Core.Capabilities.Content;
using Files.Core.Models;

namespace Files.Core.Storage.Archives;

internal static class ArchiveStreamResolver
{
	public static async ValueTask<Stream> OpenReadAsync(IStorableModel model, CancellationToken cancellationToken)
	{
		var content = model.Get<IReadableContent>() ?? throw new UnsupportedArchiveException(model.Name);
		var stream = content is IArchiveInput input ? await input.OpenReadAsync(cancellationToken).ConfigureAwait(false) : await MaterializeAsync(content, cancellationToken).ConfigureAwait(false);
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!stream.CanRead || !stream.CanSeek)
			{
				throw new InvalidDataException("Archive inputs must provide an independent, readable, seekable stream.");
			}

			stream.Position = 0;

			return stream;
		}
		catch
		{
			await stream.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

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

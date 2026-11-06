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
		var output = new MemoryStream();
		try
		{
			await using var input = await content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			output.Position = 0;

			return output;
		}
		catch
		{
			await output.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}
}

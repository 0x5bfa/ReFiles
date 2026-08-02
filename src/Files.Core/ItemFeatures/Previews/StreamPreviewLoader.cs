// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using Files.Core.ItemFeatures;
using OwlCore.Storage;

namespace Files.Core.ItemFeatures.Previews;

public sealed class StreamPreviewLoader : IPreviewLoader
{
	private const int CopyBufferSize = 81920;

	private readonly IPreviewContentTypeResolver contentTypeResolver;
	private readonly IPreviewStreamAccessPolicy accessPolicy;

	public StreamPreviewLoader(IPreviewContentTypeResolver contentTypeResolver, IPreviewStreamAccessPolicy accessPolicy)
	{
		ArgumentNullException.ThrowIfNull(contentTypeResolver);
		ArgumentNullException.ThrowIfNull(accessPolicy);

		this.contentTypeResolver = contentTypeResolver;
		this.accessPolicy = accessPolicy;
	}

	public bool CanLoad(ItemContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return context.CoreModel is IFile
			&& contentTypeResolver.TryResolve(context, out _);
	}

	public async ValueTask<PreviewResult?> GetPreviewAsync(PreviewRequest request, ItemContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		if (context.CoreModel is not IFile file
			|| !contentTypeResolver.TryResolve(context, out var contentType))
		{
			return null;
		}

		var blockReason = await accessPolicy.GetBlockReasonAsync(request, context, cancellationToken).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();

		if (blockReason is not null)
		{
			return new BlockedPreviewResult(blockReason.Value);
		}

		var stream = await file.OpenStreamAsync(FileAccess.Read, cancellationToken).ConfigureAwait(false);

		return await CreateResultAsync(stream, request.MaximumBytes, contentType.MediaType, file.Name, cancellationToken).ConfigureAwait(false);
	}

	private static async ValueTask<PreviewResult> CreateResultAsync(
		Stream stream,
		long? maximumBytes,
		string contentType,
		string suggestedFileName,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);

		var sourceOwned = true;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (maximumBytes is null)
			{
				var contentLength = TryGetLength(stream, out var unboundedLength)
					? (long?)unboundedLength
					: null;
				var result = new StreamPreviewResult(stream, contentType, contentLength, suggestedFileName);
				sourceOwned = false;
				return result;
			}

			if (TryGetLength(stream, out var length))
			{
				if (length > maximumBytes.Value)
				{
					return new BlockedPreviewResult(PreviewBlockReason.TooLarge);
				}

				var result = new StreamPreviewResult(stream, contentType, length, suggestedFileName);
				sourceOwned = false;
				return result;
			}

			return await BufferNonSeekableAsync(
				stream,
				maximumBytes.Value,
				contentType,
				suggestedFileName,
				cancellationToken,
				() => sourceOwned = false).ConfigureAwait(false);
		}
		finally
		{
			if (sourceOwned)
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private static async ValueTask<PreviewResult> BufferNonSeekableAsync(
		Stream stream,
		long maximumBytes,
		string contentType,
		string suggestedFileName,
		CancellationToken cancellationToken,
		Action markSourceDisposed)
	{
		var buffer = new MemoryStream();
		var bufferOwned = true;
		var bytesRead = 0L;
		var readBuffer = new byte[CopyBufferSize];
		var limit = maximumBytes == long.MaxValue
			? long.MaxValue
			: maximumBytes + 1;

		try
		{
			while (bytesRead < limit)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var requested = (int)Math.Min(readBuffer.Length, limit - bytesRead);
				var count = await stream.ReadAsync(readBuffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);

				if (count == 0)
				{
					break;
				}

				await buffer.WriteAsync(readBuffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
				bytesRead += count;
			}

			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				await stream.DisposeAsync().ConfigureAwait(false);
			}
			finally
			{
				markSourceDisposed();
			}

			if (bytesRead > maximumBytes)
			{
				return new BlockedPreviewResult(PreviewBlockReason.TooLarge);
			}

			buffer.Position = 0;
			var result = new StreamPreviewResult(buffer, contentType, bytesRead, suggestedFileName);
			bufferOwned = false;
			return result;
		}
		finally
		{
			if (bufferOwned)
			{
				await buffer.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	private static bool TryGetLength(Stream stream, out long length)
	{
		if (!stream.CanSeek)
		{
			length = 0;
			return false;
		}

		try
		{
			length = stream.Length;
			return true;
		}
		catch (NotSupportedException)
		{
			length = 0;
			return false;
		}
		catch (InvalidOperationException)
		{
			length = 0;
			return false;
		}
	}
}

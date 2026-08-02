// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.ItemFeatures.Previews;

/// <summary>
/// Owns an encoded or textual preview stream.
/// </summary>
public sealed class StreamPreviewResult : PreviewResult
{
	private Stream? _content;

	public Stream Content => Volatile.Read(ref _content) ?? throw new ObjectDisposedException(nameof(StreamPreviewResult));

	public string ContentType { get; }

	public long? ContentLength { get; }

	public string? SuggestedFileName { get; }

	public StreamPreviewResult(Stream content, string contentType, long? contentLength = null, string? suggestedFileName = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (!content.CanRead)
		{
			throw new ArgumentException("The preview stream must be readable.", nameof(content));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

		if (contentLength is not null)
		{
			ArgumentOutOfRangeException.ThrowIfNegative(contentLength.Value);

		}

		if (suggestedFileName is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

		}

		_content = content;
		ContentType = contentType;
		ContentLength = contentLength;
		SuggestedFileName = suggestedFileName;
	}

	public override async ValueTask DisposeAsync()
	{
		var stream = Interlocked.Exchange(ref _content, null);
		if (stream is not null)
		{
			await stream.DisposeAsync().ConfigureAwait(false);
		}
	}
}

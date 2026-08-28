// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Owns an encoded or textual preview stream.
/// </summary>
public sealed class StreamPreviewResult : PreviewResult
{
	private Stream? _content;

	/// <summary>Gets the readable preview stream.</summary>
	public Stream Content => Volatile.Read(ref _content) ?? throw new ObjectDisposedException(nameof(StreamPreviewResult));

	/// <summary>Gets the MIME type of the stream content.</summary>
	public string ContentType { get; }

	/// <summary>Gets the length of the stream content, when known.</summary>
	public long? ContentLength { get; }

	/// <summary>Gets the suggested file name, when provided.</summary>
	public string? SuggestedFileName { get; }

	/// <summary>Initializes a stream preview result.</summary>
	/// <param name="content">The readable preview stream.</param>
	/// <param name="contentType">The MIME type of the stream content.</param>
	/// <param name="contentLength">The optional content length.</param>
	/// <param name="suggestedFileName">The optional suggested file name.</param>
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

	/// <summary>Disposes the preview stream.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public override async ValueTask DisposeAsync()
	{
		var stream = Interlocked.Exchange(ref _content, null);
		if (stream is not null)
		{
			await stream.DisposeAsync().ConfigureAwait(false);
		}
	}
}

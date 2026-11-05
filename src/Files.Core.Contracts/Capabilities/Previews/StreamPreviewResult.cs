// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Capabilities.Previews;

/// <summary>
/// Owns an encoded or textual preview stream.
/// </summary>
public sealed class StreamPreviewResult : PreviewResult
{
	private readonly Lock _syncRoot = new();

	private Stream? _content;
	private TaskCompletionSource<object?>? _disposeCompletion;
	private Task? _disposeTask;
	private int _activeLeaseCount;
	private bool _disposeRequested;

	/// <summary>
	/// Gets the readable preview stream. Call <see cref="AcquireContent"/> before reading when the result may be disposed concurrently. Concurrent reads share the stream position and are not supported.
	/// </summary>
	public Stream Content
	{
		get
		{
			lock (_syncRoot)
			{
				if (_disposeRequested || _content is null)
				{
					throw new ObjectDisposedException(nameof(StreamPreviewResult));
				}

				return _content;
			}
		}
	}

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

	/// <summary>Acquires the preview stream for the duration of a read operation. Leases share one mutable stream and must not be read concurrently.</summary>
	/// <returns>A lease that keeps the preview stream alive. Dispose the lease instead of its <see cref="StreamPreviewContentLease.Content"/> stream.</returns>
	public StreamPreviewContentLease AcquireContent()
	{
		lock (_syncRoot)
		{
			if (_disposeRequested || _content is null)
			{
				throw new ObjectDisposedException(nameof(StreamPreviewResult));
			}

			_activeLeaseCount++;

			return new StreamPreviewContentLease(this, _content);
		}
	}

	/// <summary>Disposes the preview stream.</summary>
	/// <returns>A value task that represents the disposal operation.</returns>
	public override ValueTask DisposeAsync()
	{
		Stream? content = null;
		TaskCompletionSource<object?> completion;
		lock (_syncRoot)
		{
			if (_disposeTask is not null)
			{
				return new ValueTask(_disposeTask);
			}

			_disposeRequested = true;
			completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_disposeCompletion = completion;
			_disposeTask = completion.Task;
			if (_activeLeaseCount is 0)
			{
				content = _content;
				_content = null;
			}
		}

		if (content is not null)
		{
			_ = DisposeContentAsync(content, completion);
		}

		return new ValueTask(completion.Task);
	}

	internal void ReleaseContent()
	{
		Stream? content = null;
		TaskCompletionSource<object?>? completion = null;
		lock (_syncRoot)
		{
			if (_activeLeaseCount <= 0)
			{
				throw new InvalidOperationException("The preview stream lease count is invalid.");
			}

			_activeLeaseCount--;
			if (_activeLeaseCount is 0 && _disposeRequested)
			{
				content = _content;
				_content = null;
				completion = _disposeCompletion;
			}
		}

		if (content is not null && completion is not null)
		{
			_ = DisposeContentAsync(content, completion);
		}
	}

	private static async Task DisposeContentAsync(Stream content, TaskCompletionSource<object?> completion)
	{
		try
		{
			await content.DisposeAsync().ConfigureAwait(false);
			completion.TrySetResult(null);
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
	}
}

/// <summary>Keeps a stream preview result alive while its content is being read.</summary>
public sealed class StreamPreviewContentLease : IDisposable
{
	private StreamPreviewResult? _owner;

	/// <summary>Gets the leased preview stream.</summary>
	public Stream Content { get; }

	internal StreamPreviewContentLease(StreamPreviewResult owner, Stream content)
	{
		_owner = owner;
		Content = content;
	}

	/// <summary>Releases the preview stream lease.</summary>
	public void Dispose()
	{
		Interlocked.Exchange(ref _owner, null)?.ReleaseContent();
	}
}

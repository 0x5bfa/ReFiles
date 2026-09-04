// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities.Previews;

namespace Files.UnitTests;

/// <summary>
/// Contains tests for stream preview result ownership.
/// </summary>
[TestClass]
public sealed class StreamPreviewResultTests
{
	/// <summary>
	/// Test case: disposal waits for active readers and closes the stream once.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task DisposalWaitsForActiveLeasesAndClosesTheStreamOnce()
	{
		var stream = new TrackingStream();
		var result = new StreamPreviewResult(stream, "text/plain");
		using var firstLease = result.AcquireContent();
		using var secondLease = result.AcquireContent();

		var disposal = result.DisposeAsync().AsTask();

		Assert.IsFalse(disposal.IsCompleted);
		Assert.AreEqual(0, stream.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => result.AcquireContent());
		Assert.Throws<ObjectDisposedException>(() => _ = result.Content);
		firstLease.Dispose();
		Assert.IsFalse(disposal.IsCompleted);
		secondLease.Dispose();
		await disposal;
		await result.DisposeAsync();

		Assert.AreEqual(1, stream.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => result.AcquireContent());
	}

	private sealed class TrackingStream : MemoryStream
	{
		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DisposeCount++;
			}

			base.Dispose(disposing);
		}
	}
}

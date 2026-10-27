// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Capabilities.Content;

/// <summary>Provides optional write access independently of read access and storage kind.</summary>
public interface IWritableContent
{
	/// <summary>Opens an owned stream for writing.</summary>
	/// <param name="cancellationToken">The token used to cancel opening the stream.</param>
	/// <returns>A writable stream that the caller must dispose.</returns>
	/// <remarks>
	/// Keep the item model and storage source alive until the stream is disposed unless the provider documents an independent lifetime.
	/// Cancellation after ownership transfers does not dispose the stream. Existing content is not guaranteed to be truncated.
	/// Use storage operations for replacement semantics. Capability presence does not guarantee current write permission.
	/// </remarks>
	Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);
}

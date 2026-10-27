// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Capabilities.Content;

/// <summary>Provides optional read access to an item's content independently of its storage kind.</summary>
public interface IReadableContent
{
	/// <summary>Opens an owned stream for reading.</summary>
	/// <param name="cancellationToken">The token used to cancel opening the stream.</param>
	/// <returns>A readable stream that the caller must dispose.</returns>
	/// <remarks>
	/// Keep the item model and storage source alive until the stream is disposed unless the provider documents an independent lifetime.
	/// Cancellation after ownership transfers does not dispose the stream. Seeking and length are optional stream features.
	/// Capability presence describes supported access, not current permission or availability.
	/// </remarks>
	Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.Core.Storage.Archives;

/// <summary>Provides a provider-validated, independently owned seekable archive input.</summary>
public interface IArchiveInput
{
	/// <summary>Opens the caller-owned archive input.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The readable, seekable input.</returns>
	Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

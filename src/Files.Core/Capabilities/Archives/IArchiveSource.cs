// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;
using Files.Core.Models;

namespace Files.Core.Capabilities.Archives;

/// <summary>
/// Opens archive application models independently of browsing and presentation.
/// </summary>
public interface IArchiveSource
{
	/// <summary>Gets the archive reference represented by the item.</summary>
	StorableReference Archive { get; }

	/// <summary>Gets the reopenable container chain.</summary>
	ArchiveLocator Locator { get; }

	/// <summary>Opens an independently owned archive view using current container content.</summary>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The archive model owned by the caller. The capability itself remains borrowed.</returns>
	ValueTask<ArchiveModel> OpenAsync(CancellationToken cancellationToken = default);
}

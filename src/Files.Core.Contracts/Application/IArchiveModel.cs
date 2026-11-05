// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;
using System.IO;

namespace Files.Core.Models;

/// <summary>Provides consumer access to ArchiveModel state and operations.</summary>
public interface IArchiveModel : IAsyncDisposable
{
	/// <summary>Gets the reopenable locator. Reopening reads current content rather than retaining this session.</summary>
	ArchiveLocator Locator { get; }

	/// <summary>Gets the container reference observed while opening this view.</summary>
	StorableReference Container { get; }

	/// <summary>Gets the borrowed root folder, valid until this model is disposed.</summary>
	IFolderModel Root { get; }

	/// <summary>Gets every physical record, including duplicates and paths that cannot be projected into folders.</summary>
	IReadOnlyList<ArchiveEntryInfo> Entries { get; }

	/// <summary>Opens an owned folder model inside this view.</summary>
	/// <param name="path">The normalized archive folder path.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>A folder owned by the caller and borrowing this archive's lifetime.</returns>
	ValueTask<IFolderModel> OpenFolderAsync(string path, CancellationToken cancellationToken = default);

	/// <summary>Resolves an exact item reference from this view.</summary>
	/// <param name="reference">The reference, scoped to this open session.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The owned item model, borrowing this archive's lifetime.</returns>
	ValueTask<IStorableModel> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}

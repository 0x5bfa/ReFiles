// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Storage.Archives;

/// <summary>
/// Owns one selected archive backend and every item exposed by that backend.
/// </summary>
public interface IArchiveMount : IAsyncDisposable
{
	/// <summary>Gets the selected backend identifier.</summary>
	string BackendId { get; }

	/// <summary>Gets the archive reference.</summary>
	StorableReference Archive { get; }

	/// <summary>Gets the storage source that exposes mounted items.</summary>
	IStorageSource ItemSource { get; }

	/// <summary>Gets the mounted archive root folder.</summary>
	IFolder Root { get; }

	/// <summary>Resolves an entry path to a storage item.</summary>
	/// <param name="entryPath">The archive entry path.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved item.</returns>
	ValueTask<IStorable> ResolveAsync(string entryPath, CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;
using Files.Core.Storage.Archives;

namespace Files.Core.Models;

/// <summary>Creates presentation models from storage-layer items.</summary>
public interface IStorableModelFactory
{
	/// <summary>Creates a model for a storage item.</summary>
	/// <param name="source">The storage source that owns the item.</param>
	/// <param name="coreModel">The storage-layer item.</param>
	/// <returns>The corresponding presentation model.</returns>
	IStorableModel Create(IStorageSource source, IStorable coreModel);

	/// <summary>Opens an archive through runtime-owned model adaptation.</summary>
	/// <param name="source">The borrowed outer source.</param>
	/// <param name="locator">The reopenable container chain.</param>
	/// <param name="readers">The configured readers.</param>
	/// <param name="credentials">The optional credential resolver.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The independently owned archive model.</returns>
	ValueTask<IArchiveModel> OpenArchiveAsync(IStorageSource source, ArchiveLocator locator, ArchiveBackendSelector readers, IArchiveCredentialResolver? credentials = null,
		CancellationToken cancellationToken = default)
	{
		return ValueTask.FromException<IArchiveModel>(new NotSupportedException("This model factory does not support archive adaptation."));
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Opens archives using one reader technology.</summary>
public interface IArchiveBackend
{
	/// <summary>Gets the unique backend identifier.</summary>
	string Id { get; }

	/// <summary>Gets the backend priority.</summary>
	int Priority { get; }

	/// <summary>Attempts to open a borrowed input without publishing storage items.</summary>
	/// <param name="request">The reader request.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The reader result; only Unsupported permits trying another backend.</returns>
	ValueTask<ArchiveOpenResult> TryOpenAsync(ArchiveOpenRequest request, CancellationToken cancellationToken = default);
}

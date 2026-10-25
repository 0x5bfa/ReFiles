// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Models;

/// <summary>Provides folder models for an interactive enumeration.</summary>
public interface IInteractiveFolderModel : IFolderModel
{
	/// <summary>Enumerates child models with an optional native interaction owner.</summary>
	/// <param name="type">The item types to include.</param>
	/// <param name="ownerWindowHandle">The native owner, or zero to suppress interaction.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The child models owned by the caller.</returns>
	IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type, nint ownerWindowHandle, CancellationToken cancellationToken = default);
}

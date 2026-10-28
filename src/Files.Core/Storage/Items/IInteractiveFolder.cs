// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>Supports enumeration that may display provider UI owned by a native window.</summary>
public interface IInteractiveFolder : IFolder
{
	/// <summary>Enumerates children with an optional native interaction owner.</summary>
	/// <param name="type">The item types to include.</param>
	/// <param name="ownerWindowHandle">The native owner, or zero to suppress interaction.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The child items owned by the caller.</returns>
	IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type, nint ownerWindowHandle, CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Models;

public interface IFolderModel : IStorableModel
{
	IFolder Folder { get; }

	IAsyncEnumerable<IStorableModel> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default);

	ValueTask<IFolderModel?> GetParentAsync(CancellationToken cancellationToken = default);
}

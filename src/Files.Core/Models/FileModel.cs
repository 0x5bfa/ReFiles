// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

public sealed class FileModel : StorableModel, IFileModel
{
	public IFile File { get; }

	public FileModel(IFile file, StorableReference reference, IItemFeatures features)
		: base(file, reference, features)
	{
		File = file;
	}
}

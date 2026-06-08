// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using OwlCore.Storage;

namespace Files.Core.Models;

public interface IFileModel : IStorableModel
{
	IFile File { get; }
}

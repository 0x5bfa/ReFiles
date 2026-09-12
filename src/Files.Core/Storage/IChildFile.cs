// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Represents a file that resides within a traversable folder structure.
/// </summary>
public interface IChildFile : IFile, IStorableChild
{
}

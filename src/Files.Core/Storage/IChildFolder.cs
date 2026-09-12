// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage;

/// <summary>
/// Represents a folder that resides within a traversable folder structure.
/// </summary>
public interface IChildFolder : IFolder, IStorableChild
{
}

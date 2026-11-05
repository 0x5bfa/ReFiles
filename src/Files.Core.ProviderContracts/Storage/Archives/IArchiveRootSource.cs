// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Storage.Archives;

/// <summary>Identifies the configured outer source behind a mounted archive.</summary>
public interface IArchiveRootSource
{
	/// <summary>Gets the borrowed source containing the outermost archive.</summary>
	IStorageSource RootSource { get; }
}

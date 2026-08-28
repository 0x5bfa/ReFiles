// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;
using Files.Core.Storage;
using OwlCore.Storage;

namespace Files.Core.Models;

/// <summary>
/// Represents a Files item AppModel for a file.
/// </summary>
public sealed class FileModel : StorableModel, IFileModel
{
	/// <summary>
	/// Initializes a Files file model.
	/// </summary>
	/// <param name="file">The owned OwlCore file.</param>
	/// <param name="reference">The stable Files item reference.</param>
	/// <param name="capabilities">The owned composed item capabilities.</param>
	public FileModel(IFile file, StorableReference reference, ICapabilities capabilities)
		: base(file, reference, capabilities)
	{
	}
}

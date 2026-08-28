// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Storage;

namespace Files.Core.Capabilities.Previews;

/// <summary>Resolves items to Windows preview targets.</summary>
public interface IWindowsPreviewTargetResolver
{
	/// <summary>Resolves a preview target for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved preview target.</returns>
	ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}

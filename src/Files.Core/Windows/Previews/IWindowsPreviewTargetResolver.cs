// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

#pragma warning disable IDE0130 // Windows APIs share a namespace across responsibility folders.

using Files.Core.Storage;

namespace Files.Core.Windows;

/// <summary>Resolves items to Windows preview targets.</summary>
public interface IWindowsPreviewTargetResolver
{
	/// <summary>Resolves a preview target for a storage reference.</summary>
	/// <param name="reference">The storage reference.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>The resolved preview target, including an <see cref="ItemContext"/> that can be used to revalidate policy before handler activation.</returns>
	ValueTask<WindowsPreviewTarget> ResolveAsync(StorableReference reference, CancellationToken cancellationToken = default);
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.Capabilities;

namespace Files.Core.Capabilities.Previews;

/// <summary>Resolves preview content types from item metadata.</summary>
public interface IPreviewContentTypeResolver
{
	/// <summary>Attempts to resolve the preview content type for an item.</summary>
	/// <param name="context">The item context.</param>
	/// <param name="contentType">Receives the resolved content type.</param>
	/// <returns><see langword="true"/> when a content type was resolved.</returns>
	bool TryResolve(ItemContext context, out PreviewContentType contentType);
}

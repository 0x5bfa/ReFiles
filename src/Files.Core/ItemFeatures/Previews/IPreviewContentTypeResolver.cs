// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Core.ItemFeatures;

namespace Files.Core.ItemFeatures.Previews;

public interface IPreviewContentTypeResolver
{
	bool TryResolve(
		ItemContext context,
		out PreviewContentType contentType);
}

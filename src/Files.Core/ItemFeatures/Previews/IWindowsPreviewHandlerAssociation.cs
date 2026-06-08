// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.ItemFeatures.Previews;

public interface IWindowsPreviewHandlerAssociation
{
	string? QueryPreviewHandler(string normalizedExtension);
}

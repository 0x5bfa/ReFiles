// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.ViewModels;

namespace Files.ItemProperties;

internal interface IItemPropertiesService
{
	Task ShowAsync(IReadOnlyList<BrowseItemViewModel> items);
}

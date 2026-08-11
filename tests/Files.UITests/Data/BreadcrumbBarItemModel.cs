// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;

namespace Files.UITests.Data
{
	internal record BreadcrumbBarItemModel(string Text, ObservableCollection<BreadcrumbBarItemModel>? Children = null);
}

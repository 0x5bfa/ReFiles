// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class DetailsPropertyView : UserControl
{
	internal ItemPropertiesViewModel ViewModel { get; }

	internal DetailsPropertyView(ItemPropertiesViewModel viewModel)
	{
		ViewModel = viewModel;
		InitializeComponent();
	}
}

// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Files.ItemProperties;

public sealed partial class ElevationShieldIcon : UserControl
{
	internal ImageSource? Source
	{
		get => ShieldImage.Source;
		set => ShieldImage.Source = value;
	}

	public ElevationShieldIcon()
	{
		InitializeComponent();
	}
}

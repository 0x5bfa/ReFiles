// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.Adapters;
using Files.Core.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Files.ItemProperties;

public sealed partial class ElevationShieldIcon : UserControl
{
	private static readonly Lazy<ReadOnlyMemory<byte>> _shieldIcon = new(() => WindowsShellIconProvider.GetElevationShieldIcon());

	public ElevationShieldIcon()
	{
		InitializeComponent();
		Loaded += ElevationShieldIcon_Loaded;
	}

	private async void ElevationShieldIcon_Loaded(object sender, RoutedEventArgs e)
	{
		Loaded -= ElevationShieldIcon_Loaded;
		if (!_shieldIcon.Value.IsEmpty)
		{
			ShieldImage.Source = await ThumbnailImageFactory.CreateAsync(_shieldIcon.Value);
		}
	}
}
